namespace iMobileDevice.Managed.Bonjour;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Channels;

internal readonly record struct MdnsReceivedPacket(
    ReadOnlyMemory<byte> Payload,
    int InterfaceIndex,
    string? InterfaceName);

internal interface IMdnsTransport : IAsyncDisposable
{
    Task SendQueryAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);
    ValueTask<MdnsReceivedPacket?> ReceiveAsync(CancellationToken cancellationToken = default);
}

internal sealed class UdpMdnsTransport : IMdnsTransport
{
    internal const int MdnsPort = 5353;
    internal static readonly IPAddress MulticastIPv4 = IPAddress.Parse("224.0.0.251");
    internal static readonly IPAddress MulticastIPv6 = IPAddress.Parse("ff02::fb");
    private static readonly byte[] MulticastIPv6Bytes = MulticastIPv6.GetAddressBytes();

    private readonly Socket? ipv4Socket;
    private readonly Socket? ipv6Socket;
    private readonly List<IPv4InterfaceTarget> ipv4Targets = [];
    private readonly List<int> ipv6InterfaceIndices = [];
    private readonly Dictionary<int, string> interfaceNames = [];
    private readonly Channel<MdnsReceivedPacket> incomingChannel;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly List<Task> receiveLoops = [];
    private int activeReaders;
    private int disposed;

    public UdpMdnsTransport()
    {
        EnumerateMulticastInterfaces();

        Exception? bindFailure = null;

        if (Socket.OSSupportsIPv4)
        {
            try
            {
                ipv4Socket = CreateBoundIPv4Socket();
            }
            catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
            {
                bindFailure = ex;
                ipv4Socket?.Dispose();
                ipv4Socket = null;
            }
        }

        if (Socket.OSSupportsIPv6)
        {
            try
            {
                ipv6Socket = CreateBoundIPv6Socket();
            }
            catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
            {
                bindFailure ??= ex;
                ipv6Socket?.Dispose();
                ipv6Socket = null;
            }
        }

        if (ipv4Socket is null && ipv6Socket is null)
        {
            lifetimeCts.Dispose();
            throw new BonjourException(
                $"Failed to bind UDP port {MdnsPort} for mDNS discovery on any IP address family.",
                bindFailure);
        }

        incomingChannel = Channel.CreateBounded<MdnsReceivedPacket>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        activeReaders = (ipv4Socket is null ? 0 : 1) + (ipv6Socket is null ? 0 : 1);
        if (ipv4Socket is not null)
        {
            receiveLoops.Add(RunReceiveLoopAsync(ipv4Socket, isIPv6: false));
        }

        if (ipv6Socket is not null)
        {
            receiveLoops.Add(RunReceiveLoopAsync(ipv6Socket, isIPv6: true));
        }
    }

    public async Task SendQueryAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (ipv4Socket is not null)
        {
            var endpointV4 = new IPEndPoint(MulticastIPv4, MdnsPort);
            if (ipv4Targets.Count == 0)
            {
                try
                {
                    await ipv4Socket.SendToAsync(packet, SocketFlags.None, endpointV4, cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                }
            }
            else
            {
                foreach (IPv4InterfaceTarget target in ipv4Targets)
                {
                    try
                    {
                        ipv4Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, target.AddressBytes);
                        await ipv4Socket.SendToAsync(packet, SocketFlags.None, endpointV4, cancellationToken).ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
        }

        if (ipv6Socket is not null)
        {
            if (ipv6InterfaceIndices.Count == 0)
            {
                try
                {
                    await ipv6Socket.SendToAsync(packet, SocketFlags.None, new IPEndPoint(MulticastIPv6, MdnsPort), cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                }
            }
            else
            {
                foreach (int ifIndex in ipv6InterfaceIndices)
                {
                    try
                    {
                        ipv6Socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, ifIndex);
                        var scopedMulticast = new IPAddress(MulticastIPv6Bytes, ifIndex);
                        await ipv6Socket.SendToAsync(packet, SocketFlags.None, new IPEndPoint(scopedMulticast, MdnsPort), cancellationToken).ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
        }
    }

    public async ValueTask<MdnsReceivedPacket?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        while (await incomingChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (incomingChannel.Reader.TryRead(out MdnsReceivedPacket packet))
            {
                return packet;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            ipv4Socket?.Dispose();
        }
        catch
        {
        }

        try
        {
            ipv6Socket?.Dispose();
        }
        catch
        {
        }

        incomingChannel.Writer.TryComplete();

        if (receiveLoops.Count > 0)
        {
            try
            {
                await Task.WhenAll(receiveLoops).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        lifetimeCts.Dispose();
    }

    private void EnumerateMulticastInterfaces()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return;
        }

        foreach (NetworkInterface nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                !nic.SupportsMulticast ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties ipProps;
            try
            {
                ipProps = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            if (nic.Supports(NetworkInterfaceComponent.IPv4))
            {
                try
                {
                    IPv4InterfaceProperties v4Props = ipProps.GetIPv4Properties();
                    if (v4Props.Index > 0)
                    {
                        interfaceNames.TryAdd(v4Props.Index, nic.Name);
                    }

                    foreach (UnicastIPAddressInformation unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(unicast.Address))
                        {
                            ipv4Targets.Add(new IPv4InterfaceTarget(v4Props.Index, unicast.Address, unicast.Address.GetAddressBytes()));
                        }
                    }
                }
                catch (NetworkInformationException)
                {
                }
            }

            if (nic.Supports(NetworkInterfaceComponent.IPv6))
            {
                try
                {
                    IPv6InterfaceProperties v6Props = ipProps.GetIPv6Properties();
                    if (v6Props.Index > 0)
                    {
                        interfaceNames.TryAdd(v6Props.Index, nic.Name);
                        if (!ipv6InterfaceIndices.Contains(v6Props.Index))
                        {
                            ipv6InterfaceIndices.Add(v6Props.Index);
                        }
                    }
                }
                catch (NetworkInformationException)
                {
                }
            }
        }
    }

    private Socket CreateBoundIPv4Socket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));

            if (ipv4Targets.Count == 0)
            {
                TryJoinIPv4Group(socket, IPAddress.Any);
            }
            else
            {
                var joinedAddresses = new HashSet<IPAddress>();
                foreach (IPv4InterfaceTarget target in ipv4Targets)
                {
                    if (joinedAddresses.Add(target.LocalAddress))
                    {
                        TryJoinIPv4Group(socket, target.LocalAddress);
                    }
                }
            }

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private Socket CreateBoundIPv6Socket()
    {
        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.DualMode = false;
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.PacketInformation, true);
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 255);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, MdnsPort));

            if (ipv6InterfaceIndices.Count == 0)
            {
                TryJoinIPv6Group(socket, 0);
            }
            else
            {
                foreach (int ifIndex in ipv6InterfaceIndices)
                {
                    TryJoinIPv6Group(socket, ifIndex);
                }
            }

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static void TryJoinIPv4Group(Socket socket, IPAddress localAddress)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastIPv4, localAddress));
        }
        catch (SocketException)
        {
        }
    }

    private static void TryJoinIPv6Group(Socket socket, int ifIndex)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(MulticastIPv6, ifIndex));
        }
        catch (SocketException)
        {
        }
    }

    private async Task RunReceiveLoopAsync(Socket socket, bool isIPv6)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(65535);
        EndPoint remoteAny = new IPEndPoint(isIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

        try
        {
            while (!lifetimeCts.IsCancellationRequested)
            {
                SocketReceiveMessageFromResult result = await socket.ReceiveMessageFromAsync(
                    buffer,
                    SocketFlags.None,
                    remoteAny,
                    lifetimeCts.Token).ConfigureAwait(false);

                if (result.ReceivedBytes <= 0)
                {
                    continue;
                }

                int ifIndex = result.PacketInformation.Interface;
                if (ifIndex <= 0 &&
                    result.RemoteEndPoint is IPEndPoint { Address.IsIPv6LinkLocal: true, Address.ScopeId: > 0 } ep)
                {
                    ifIndex = (int)ep.Address.ScopeId;
                }

                string? ifName = ifIndex > 0 && interfaceNames.TryGetValue(ifIndex, out string? resolvedName)
                    ? resolvedName
                    : null;

                byte[] payload = buffer.AsSpan(0, result.ReceivedBytes).ToArray();
                incomingChannel.Writer.TryWrite(new MdnsReceivedPacket(payload, ifIndex, ifName));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            if (Interlocked.Decrement(ref activeReaders) == 0)
            {
                incomingChannel.Writer.TryComplete();
            }
        }
    }

    private readonly record struct IPv4InterfaceTarget(int InterfaceIndex, IPAddress LocalAddress, byte[] AddressBytes);
}
