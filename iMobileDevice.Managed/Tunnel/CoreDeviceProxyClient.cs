namespace iMobileDevice.Managed.Tunnel;

using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using iMobileDevice.Managed.Lockdown;

public sealed record CoreDeviceTunnelParameters(
    IPAddress ClientAddress,
    IPAddress ServerAddress,
    ushort ServerRsdPort,
    int Mtu,
    string? Netmask);

/// <summary>
/// Pure managed client for iOS 17.4+ <c>com.apple.internal.devicecompute.CoreDeviceProxy</c>,
/// negotiating the <c>CDTunnel</c> handshake and providing raw L3 IPv6 packet I/O over the TLS tunnel.
/// </summary>
public sealed class CoreDeviceProxyClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.internal.devicecompute.CoreDeviceProxy";
    private static ReadOnlySpan<byte> HandshakeMagic => "CDTunnel"u8;

    private readonly Stream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private bool disposed;

    private CoreDeviceProxyClient(Stream stream, CoreDeviceTunnelParameters parameters)
    {
        this.stream = stream;
        Parameters = parameters;
    }

    public CoreDeviceTunnelParameters Parameters { get; }

    public static async Task<CoreDeviceProxyClient> ConnectAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            CoreDeviceTunnelParameters parameters = await PerformHandshakeAsync(serviceStream, cancellationToken).ConfigureAwait(false);
            return new CoreDeviceProxyClient(serviceStream, parameters);
        }
        catch
        {
            await serviceStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<CoreDeviceTunnelParameters> PerformHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Send client CDTunnel handshake request
        byte[] requestJson = Encoding.UTF8.GetBytes("{\"type\":\"clientHandshakeRequest\",\"mtu\":16000}");
        byte[] requestPacket = new byte[HandshakeMagic.Length + 2 + requestJson.Length];
        HandshakeMagic.CopyTo(requestPacket);
        BinaryPrimitives.WriteUInt16BigEndian(requestPacket.AsSpan(8, 2), (ushort)requestJson.Length);
        requestJson.CopyTo(requestPacket.AsSpan(10));

        await stream.WriteAsync(requestPacket, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Read server CDTunnel handshake response
        byte[] header = new byte[10];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 8).SequenceEqual(HandshakeMagic))
        {
            throw new InvalidDataException($"Unexpected CoreDeviceProxy handshake magic: {Convert.ToHexString(header.AsSpan(0, 8))}");
        }

        ushort bodyLen = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8, 2));
        byte[] body = new byte[bodyLen];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);

        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("error", out JsonElement errEl))
        {
            throw new InvalidOperationException($"CoreDeviceProxy handshake error: {errEl}");
        }

        JsonElement clientParams = root.GetProperty("clientParameters");
        IPAddress clientAddr = IPAddress.Parse(clientParams.GetProperty("address").GetString()!);
        string? netmask = clientParams.TryGetProperty("netmask", out JsonElement nm) ? nm.GetString() : null;
        int mtu = clientParams.TryGetProperty("mtu", out JsonElement mtuEl) ? mtuEl.GetInt32() : 16000;
        IPAddress serverAddr = IPAddress.Parse(root.GetProperty("serverAddress").GetString()!);
        ushort rsdPort = root.GetProperty("serverRSDPort").GetUInt16();

        return new CoreDeviceTunnelParameters(clientAddr, serverAddr, rsdPort, mtu, netmask);
    }

    /// <summary>
    /// Writes a single raw IPv6 L3 packet into the CoreDeviceProxy tunnel.
    /// </summary>
    public async Task WritePacketAsync(ReadOnlyMemory<byte> ipv6Packet, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(ipv6Packet, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Reads the next complete raw IPv6 L3 packet from the CoreDeviceProxy tunnel.
    /// Returns <c>null</c> on clean EOF.
    /// </summary>
    public async Task<byte[]?> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[40];
        int first = await ReadAtLeastOrEofAsync(header, cancellationToken).ConfigureAwait(false);
        if (first == 0)
        {
            return null;
        }

        int version = (header[0] >> 4) & 0x0F;
        if (version != 6)
        {
            throw new InvalidDataException($"Expected IPv6 packet (version 6) from CoreDeviceProxy, got version {version}.");
        }

        ushort payloadLen = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        byte[] packet = new byte[40 + payloadLen];
        header.CopyTo(packet, 0);
        if (payloadLen > 0)
        {
            await stream.ReadExactlyAsync(packet.AsMemory(40, payloadLen), cancellationToken).ConfigureAwait(false);
        }

        return packet;
    }

    private async Task<int> ReadAtLeastOrEofAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.Slice(total), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                if (total == 0)
                {
                    return 0;
                }

                throw new EndOfStreamException("Unexpected EOF while reading IPv6 header from CoreDeviceProxy.");
            }

            total += n;
        }

        return total;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await stream.DisposeAsync().ConfigureAwait(false);
        writeLock.Dispose();
    }
}
