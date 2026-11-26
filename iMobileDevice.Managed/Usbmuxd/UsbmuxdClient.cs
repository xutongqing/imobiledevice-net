namespace iMobileDevice.Managed.Usbmuxd;

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Pure managed client for Apple's <c>usbmuxd</c> daemon (Unix domain socket on macOS/Linux, TCP 127.0.0.1:27015 on Windows).
/// </summary>
public sealed class UsbmuxdClient
{
    private const uint ProtocolVersionPlist = 1;
    private const uint MessageTypePlist = 8;
    private const string ClientVersionString = "iMobileDevice.Managed-1.0";
    private const string ProgName = "iMobileDevice.Managed";

    private readonly EndPoint endpoint;
    private int nextTag;

    public UsbmuxdClient(EndPoint? endpoint = null)
    {
        this.endpoint = endpoint ?? ResolveDefaultEndPoint();
    }

    public static EndPoint ResolveDefaultEndPoint()
    {
        string? envAddr = Environment.GetEnvironmentVariable("USBMUXD_SOCKET_ADDRESS");
        if (!string.IsNullOrWhiteSpace(envAddr))
        {
            if (envAddr.StartsWith("UNIX:", StringComparison.OrdinalIgnoreCase))
            {
                return new UnixDomainSocketEndPoint(envAddr.Substring(5));
            }

            if (envAddr.StartsWith("TCP:", StringComparison.OrdinalIgnoreCase))
            {
                string hostPort = envAddr.Substring(4);
                int idx = hostPort.LastIndexOf(':');
                string host = idx > 0 ? hostPort.Substring(0, idx) : "127.0.0.1";
                int port = idx > 0 ? int.Parse(hostPort.Substring(idx + 1)) : 27015;
                return new IPEndPoint(IPAddress.Parse(host), port);
            }

            return new UnixDomainSocketEndPoint(envAddr);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new IPEndPoint(IPAddress.Loopback, 27015);
        }

        return new UnixDomainSocketEndPoint("/var/run/usbmuxd");
    }

    public async Task<IReadOnlyList<UsbmuxdDeviceInfo>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        using var socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var request = CreateBaseMessage("ListDevices");
        PlistDictionary reply = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
        EnsureOkResult(reply, "ListDevices");

        PlistArray? list = reply.GetArray("DeviceList");
        if (list == null)
        {
            throw new UsbmuxdException("usbmuxd ListDevices reply did not contain DeviceList.");
        }

        var result = new List<UsbmuxdDeviceInfo>(list.Count);
        foreach (PlistNode item in list)
        {
            if (item is PlistDictionary dict && TryParseDeviceEntry(dict, out var info))
            {
                result.Add(info);
            }
        }

        return result;
    }

    public async Task<string> ReadBuidAsync(CancellationToken cancellationToken = default)
    {
        using var socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var request = CreateBaseMessage("ReadBUID");
        PlistDictionary reply = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
        EnsureOkResult(reply, "ReadBUID");

        string? buid = reply.GetString("BUID");
        if (string.IsNullOrWhiteSpace(buid))
        {
            throw new UsbmuxdException("usbmuxd did not return a BUID.");
        }

        return buid;
    }

    public async Task<PairRecord?> ReadPairRecordAsync(string udid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(udid);

        using var socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var request = CreateBaseMessage("ReadPairRecord");
        request["PairRecordID"] = udid;

        PlistDictionary reply = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
        if (string.Equals(reply.GetString("MessageType"), "Result", StringComparison.Ordinal))
        {
            int number = checked((int)(reply.GetInt64("Number") ?? -1));
            // usbmuxd returns ENOENT (2 / BadDevice) when no pair record exists for the requested UDID
            if (number == (int)UsbmuxdResultCode.BadDevice)
            {
                return null;
            }

            EnsureOkResult(reply, $"ReadPairRecord('{udid}')");
        }

        byte[]? data = reply.GetData("PairRecordData");
        if (data == null || data.Length == 0)
        {
            throw new UsbmuxdException($"usbmuxd ReadPairRecord('{udid}') reply did not contain PairRecordData.");
        }

        PlistDictionary inner = PlistSerializer.Parse(data).AsDictionary();
        return PairRecord.FromPlist(inner);
    }

    public async Task SavePairRecordAsync(string udid, PairRecord pairRecord, int? deviceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(udid);
        ArgumentNullException.ThrowIfNull(pairRecord);

        using var socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var request = CreateBaseMessage("SavePairRecord");
        request["PairRecordID"] = udid;
        request["PairRecordData"] = PlistSerializer.WriteXml(pairRecord.ToPlist());
        if (deviceId.HasValue)
        {
            request["DeviceID"] = deviceId.Value;
        }

        PlistDictionary reply = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
        EnsureOkResult(reply, "SavePairRecord");
    }

    public async Task DeletePairRecordAsync(string udid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(udid);

        using var socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var request = CreateBaseMessage("DeletePairRecord");
        request["PairRecordID"] = udid;

        PlistDictionary reply = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
        EnsureOkResult(reply, "DeletePairRecord");
    }

    /// <summary>
    /// Opens a raw stream tunneled by <c>usbmuxd</c> directly to <paramref name="port"/> on the target device.
    /// </summary>
    public async Task<NetworkStream> ConnectAsync(int deviceId, ushort port, CancellationToken cancellationToken = default)
    {
        Socket socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = new NetworkStream(socket, ownsSocket: true);
            try
            {
                var request = CreateBaseMessage("Connect");
                request["DeviceID"] = deviceId;
                // usbmuxd expects PortNumber in network byte order (big-endian) stored inside the integer
                request["PortNumber"] = (int)BinaryPrimitives.ReverseEndianness(port);

                PlistDictionary reply = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
                EnsureOkResult(reply, $"Connect(device={deviceId}, port={port})");
                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async IAsyncEnumerable<UsbmuxdDeviceEvent> WatchDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var socket = await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        var request = CreateBaseMessage("Listen");
        PlistDictionary ack = await SendAndReceivePlistAsync(stream, request, cancellationToken).ConfigureAwait(false);
        EnsureOkResult(ack, "Listen");

        while (!cancellationToken.IsCancellationRequested)
        {
            PlistDictionary msg = await ReceivePlistPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            string? msgType = msg.GetString("MessageType");
            if (string.Equals(msgType, "Attached", StringComparison.Ordinal) && TryParseDeviceEntry(msg, out var device))
            {
                yield return new UsbmuxdDeviceEvent(UsbmuxdEventKind.Attached, device.DeviceId, device);
            }
            else if (string.Equals(msgType, "Detached", StringComparison.Ordinal))
            {
                int deviceId = checked((int)(msg.GetInt64("DeviceID") ?? 0));
                yield return new UsbmuxdDeviceEvent(UsbmuxdEventKind.Detached, deviceId, null);
            }
        }
    }

    private async Task<Socket> OpenSocketAsync(CancellationToken cancellationToken)
    {
        Socket socket = endpoint is UnixDomainSocketEndPoint
            ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw new UsbmuxdException($"Failed to connect to usbmuxd at '{endpoint}'.", innerException: ex);
        }
    }

    private static PlistDictionary CreateBaseMessage(string messageType) => new()
    {
        ["MessageType"] = messageType,
        ["ClientVersionString"] = ClientVersionString,
        ["ProgName"] = ProgName,
        ["kLibUSBMuxVersion"] = 3,
    };

    private async Task<PlistDictionary> SendAndReceivePlistAsync(Stream stream, PlistDictionary request, CancellationToken cancellationToken)
    {
        uint tag = (uint)Interlocked.Increment(ref nextTag);
        byte[] payload = PlistSerializer.WriteXml(request);
        byte[] packet = new byte[16 + payload.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), ProtocolVersionPlist);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), MessageTypePlist);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), tag);
        payload.CopyTo(packet.AsSpan(16));

        await stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        return await ReceivePlistPacketAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PlistDictionary> ReceivePlistPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[16];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        uint totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (totalLength < 16 || totalLength > 32 * 1024 * 1024)
        {
            throw new UsbmuxdException($"Invalid usbmuxd packet length: {totalLength}.");
        }

        byte[] payload = new byte[totalLength - 16];
        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return PlistSerializer.Parse(payload).AsDictionary();
    }

    private static void EnsureOkResult(PlistDictionary reply, string operation)
    {
        if (string.Equals(reply.GetString("MessageType"), "Result", StringComparison.Ordinal))
        {
            int number = checked((int)(reply.GetInt64("Number") ?? -1));
            if (number != 0)
            {
                var code = Enum.IsDefined(typeof(UsbmuxdResultCode), number)
                    ? (UsbmuxdResultCode)number
                    : (UsbmuxdResultCode?)null;
                throw new UsbmuxdException($"usbmuxd {operation} failed with result code {number} ({code?.ToString() ?? "Unknown"}).", code);
            }
        }
    }

    private static bool TryParseDeviceEntry(PlistDictionary entry, out UsbmuxdDeviceInfo info)
    {
        PlistDictionary? props = entry.GetDictionary("Properties");
        if (props == null)
        {
            info = null!;
            return false;
        }

        int deviceId = checked((int)(props.GetInt64("DeviceID") ?? entry.GetInt64("DeviceID") ?? 0));
        string serial = props.GetString("SerialNumber") ?? string.Empty;
        int productId = checked((int)(props.GetInt64("ProductID") ?? 0));
        string? connTypeStr = props.GetString("ConnectionType");
        var connType = connTypeStr switch
        {
            "USB" => UsbmuxdConnectionType.Usb,
            "Network" => UsbmuxdConnectionType.Network,
            _ => UsbmuxdConnectionType.Unknown,
        };

        int? locationId = props.GetInt64("LocationID") is long loc ? unchecked((int)loc) : null;
        byte[]? netAddr = props.GetData("NetworkAddress");

        info = new UsbmuxdDeviceInfo(deviceId, serial, productId, connType, locationId, netAddr);
        return true;
    }
}
