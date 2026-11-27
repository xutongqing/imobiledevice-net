namespace iMobileDevice.Managed.Afc;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using iMobileDevice.Managed.Lockdown;

/// <summary>
/// Pure managed client for Apple File Conduit (<c>com.apple.afc</c>).
/// </summary>
public sealed partial class AfcClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.afc";
    public const int DefaultTransferChunkSize = DefaultChunkSize;
    private const int HeaderSize = 40;
    private const int DefaultChunkSize = 1024 * 1024; // 1 MiB
    private const ulong MaxPacketLength = 64UL * 1024 * 1024; // 64 MiB safety cap
    private static ReadOnlySpan<byte> Magic => "CFA6LPAA"u8;

    private readonly Stream stream;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private ulong nextPacketNumber;
    private int closed;

    public AfcClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public static async Task<AfcClient> ConnectAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new AfcClient(serviceStream);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        AfcPacket reply = await ExchangeAsync(AfcOperation.GetDeviceInfo, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureDataPacket(reply, "GetDeviceInfo");
        return ParseKeyValueList(reply.GetCombinedPayload());
    }

    public async Task<IReadOnlyList<string>> ReadDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] headerPayload = EncodeNullTerminated(path);
        AfcPacket reply = await ExchangeAsync(AfcOperation.ReadDir, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureDataPacket(reply, $"ReadDirectory('{path}')");
        return ParseStringList(reply.GetCombinedPayload());
    }

    public async Task<AfcFileInfo> GetFileInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] headerPayload = EncodeNullTerminated(path);
        AfcPacket reply = await ExchangeAsync(AfcOperation.GetFileInfo, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureDataPacket(reply, $"GetFileInfo('{path}')");
        IReadOnlyDictionary<string, string> props = ParseKeyValueList(reply.GetCombinedPayload());

        long size = props.TryGetValue("st_size", out string? s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sv) ? sv : 0;
        long blocks = props.TryGetValue("st_blocks", out string? b) && long.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bv) ? bv : 0;
        string ifmt = props.TryGetValue("st_ifmt", out string? f) ? f : string.Empty;
        DateTimeOffset? ctime = ParseUnixNanoseconds(props, "st_birthtime");
        DateTimeOffset? mtime = ParseUnixNanoseconds(props, "st_mtime");

        return new AfcFileInfo(size, blocks, ifmt, ctime, mtime, props);
    }

    public async Task MakeDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        AfcPacket reply = await ExchangeAsync(AfcOperation.MakeDir, EncodeNullTerminated(path), ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"MakeDirectory('{path}')");
    }

    public async Task RemovePathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        AfcPacket reply = await ExchangeAsync(AfcOperation.RemovePath, EncodeNullTerminated(path), ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"RemovePath('{path}')");
    }

    public async Task RemovePathAndContentsAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        AfcPacket reply = await ExchangeAsync(AfcOperation.RemovePathAndContents, EncodeNullTerminated(path), ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"RemovePathAndContents('{path}')");
    }

    public async Task RenamePathAsync(string fromPath, string toPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromPath);
        ArgumentException.ThrowIfNullOrEmpty(toPath);

        byte[] fromBytes = Encoding.UTF8.GetBytes(fromPath);
        byte[] toBytes = Encoding.UTF8.GetBytes(toPath);
        byte[] payload = new byte[fromBytes.Length + 1 + toBytes.Length + 1];
        fromBytes.CopyTo(payload, 0);
        toBytes.CopyTo(payload, fromBytes.Length + 1);

        AfcPacket reply = await ExchangeAsync(AfcOperation.RenamePath, payload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"RenamePath('{fromPath}', '{toPath}')");
    }

    public async Task<ulong> OpenFileHandleAsync(string path, AfcFileOpenMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] headerPayload = new byte[8 + pathBytes.Length + 1];
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(0, 8), (ulong)mode);
        pathBytes.CopyTo(headerPayload, 8);

        AfcPacket reply = await ExchangeAsync(AfcOperation.FileRefOpen, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        if (reply.Operation == AfcOperation.Status)
        {
            EnsureStatusOk(reply, $"OpenFile('{path}', {mode})");
        }

        if (reply.Operation != AfcOperation.FileRefOpenResult || reply.HeaderPayload.Length < 8)
        {
            throw new AfcException($"Unexpected AFC reply operation {reply.Operation} for FileRefOpen('{path}').", AfcErrorCode.UnknownPacketType);
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(reply.HeaderPayload.AsSpan(0, 8));
    }

    public async Task<byte[]> ReadFileChunkAsync(ulong handle, int maxBytes, CancellationToken cancellationToken = default)
    {
        byte[] headerPayload = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(0, 8), handle);
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(8, 8), (ulong)maxBytes);

        AfcPacket reply = await ExchangeAsync(AfcOperation.FileRefRead, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureDataPacket(reply, $"ReadFileChunk(handle={handle})");
        return reply.GetCombinedPayload();
    }

    public async Task WriteFileChunkAsync(ulong handle, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        byte[] headerPayload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(0, 8), handle);

        AfcPacket reply = await ExchangeAsync(AfcOperation.FileRefWrite, headerPayload, data, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"WriteFileChunk(handle={handle})");
    }

    public async Task SeekFileAsync(ulong handle, long offset, SeekOrigin origin, CancellationToken cancellationToken = default)
    {
        ulong whence = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => 1,
            SeekOrigin.End => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        byte[] headerPayload = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(0, 8), handle);
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(8, 8), whence);
        BinaryPrimitives.WriteInt64LittleEndian(headerPayload.AsSpan(16, 8), offset);

        AfcPacket reply = await ExchangeAsync(AfcOperation.FileRefSeek, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"SeekFile(handle={handle})");
    }

    public async Task<ulong> TellFileAsync(ulong handle, CancellationToken cancellationToken = default)
    {
        byte[] headerPayload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(0, 8), handle);

        AfcPacket reply = await ExchangeAsync(AfcOperation.FileRefTell, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        if (reply.Operation == AfcOperation.Status)
        {
            EnsureStatusOk(reply, $"TellFile(handle={handle})");
        }

        if (reply.Operation != AfcOperation.FileRefTellResult || reply.HeaderPayload.Length < 8)
        {
            throw new AfcException($"Unexpected AFC reply operation {reply.Operation} for FileRefTell.", AfcErrorCode.UnknownPacketType);
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(reply.HeaderPayload.AsSpan(0, 8));
    }

    internal async Task SetBackupLockAsync(ulong handle, bool acquire, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, handle);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8), acquire ? 6UL : 12UL);
        var reply = await ExchangeAsync(AfcOperation.FileRefLock, payload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, "FileRefLock");
    }

    public async Task CloseFileHandleAsync(ulong handle, CancellationToken cancellationToken = default)
    {
        byte[] headerPayload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(headerPayload.AsSpan(0, 8), handle);

        AfcPacket reply = await ExchangeAsync(AfcOperation.FileRefClose, headerPayload, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        EnsureStatusOk(reply, $"CloseFile(handle={handle})");
    }

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        ulong handle = await OpenFileHandleAsync(path, AfcFileOpenMode.ReadOnly, cancellationToken).ConfigureAwait(false);
        try
        {
            using var ms = new MemoryStream();
            while (true)
            {
                byte[] chunk = await ReadFileChunkAsync(handle, DefaultChunkSize, cancellationToken).ConfigureAwait(false);
                if (chunk.Length == 0)
                {
                    break;
                }

                ms.Write(chunk, 0, chunk.Length);
            }

            return ms.ToArray();
        }
        finally
        {
            if (Volatile.Read(ref closed) == 0)
            {
                await CloseFileHandleAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ulong handle = await OpenFileHandleAsync(path, AfcFileOpenMode.WriteOnly, cancellationToken).ConfigureAwait(false);
        try
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int count = Math.Min(DefaultChunkSize, data.Length - offset);
                await WriteFileChunkAsync(handle, data.Slice(offset, count), cancellationToken).ConfigureAwait(false);
                offset += count;
            }
        }
        finally
        {
            if (Volatile.Read(ref closed) == 0)
            {
                await CloseFileHandleAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task<AfcPacket> ExchangeAsync(
        AfcOperation operation,
        ReadOnlyMemory<byte> headerPayload,
        ReadOnlyMemory<byte> bodyPayload,
        CancellationToken cancellationToken)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool started = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            ulong packetNum = ++nextPacketNumber;
            ulong thisLen = (ulong)(HeaderSize + headerPayload.Length);
            ulong entireLen = thisLen + (ulong)bodyPayload.Length;

            byte[] header = new byte[HeaderSize];
            Magic.CopyTo(header.AsSpan(0, 8));
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8, 8), entireLen);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16, 8), thisLen);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24, 8), packetNum);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32, 8), (ulong)operation);

            started = true;
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (!headerPayload.IsEmpty)
            {
                await stream.WriteAsync(headerPayload, cancellationToken).ConfigureAwait(false);
            }

            if (!bodyPayload.IsEmpty)
            {
                await stream.WriteAsync(bodyPayload, cancellationToken).ConfigureAwait(false);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            AfcPacket reply = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
            if (reply.PacketNumber != packetNum)
            {
                throw new AfcException($"AFC packet sequence mismatch: expected {packetNum}, received {reply.PacketNumber}.", AfcErrorCode.OpHeaderInvalid);
            }

            return reply;
        }
        catch when (started)
        {
            // A partial frame or a late reply makes the next exchange unsafe on this connection.
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            ioLock.Release();
        }
    }

    private async Task<AfcPacket> ReadPacketAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 8).SequenceEqual(Magic))
        {
            throw new AfcException("Invalid AFC packet magic.", AfcErrorCode.OpHeaderInvalid);
        }

        ulong entireLen = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8, 8));
        ulong thisLen = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16, 8));
        ulong packetNum = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24, 8));
        var op = (AfcOperation)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32, 8));

        if (thisLen < HeaderSize || entireLen < thisLen || entireLen > MaxPacketLength)
        {
            throw new AfcException($"Invalid AFC packet lengths: this={thisLen}, entire={entireLen}.", AfcErrorCode.OpHeaderInvalid);
        }

        int headerPayloadLen = checked((int)(thisLen - HeaderSize));
        int bodyLen = checked((int)(entireLen - thisLen));

        byte[] headerPayload = headerPayloadLen > 0 ? new byte[headerPayloadLen] : Array.Empty<byte>();
        if (headerPayloadLen > 0)
        {
            await stream.ReadExactlyAsync(headerPayload, cancellationToken).ConfigureAwait(false);
        }

        byte[] body = bodyLen > 0 ? new byte[bodyLen] : Array.Empty<byte>();
        if (bodyLen > 0)
        {
            await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        }

        return new AfcPacket(packetNum, op, headerPayload, body);
    }

    private static void EnsureStatusOk(AfcPacket packet, string context)
    {
        if (packet.Operation != AfcOperation.Status)
        {
            throw new AfcException($"Expected AFC Status packet for {context}, got {packet.Operation}.", AfcErrorCode.UnknownPacketType);
        }

        AfcErrorCode code = packet.HeaderPayload.Length >= 8
            ? (AfcErrorCode)BinaryPrimitives.ReadUInt64LittleEndian(packet.HeaderPayload.AsSpan(0, 8))
            : AfcErrorCode.UnknownError;

        if (code != AfcErrorCode.Success)
        {
            throw new AfcException($"AFC {context} failed with status {code} ({(ulong)code}).", code);
        }
    }

    private static void EnsureDataPacket(AfcPacket packet, string context)
    {
        if (packet.Operation == AfcOperation.Status)
        {
            EnsureStatusOk(packet, context);
            throw new AfcException($"Expected AFC Data packet for {context}, got empty Status=Success.", AfcErrorCode.UnknownPacketType);
        }

        if (packet.Operation != AfcOperation.Data)
        {
            throw new AfcException($"Expected AFC Data packet for {context}, got {packet.Operation}.", AfcErrorCode.UnknownPacketType);
        }
    }

    private static byte[] EncodeNullTerminated(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        byte[] result = new byte[utf8.Length + 1];
        utf8.CopyTo(result, 0);
        return result;
    }

    private static IReadOnlyList<string> ParseStringList(byte[] data)
    {
        var result = new List<string>();
        int start = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0)
            {
                if (i > start)
                {
                    result.Add(Encoding.UTF8.GetString(data, start, i - start));
                }

                start = i + 1;
            }
        }

        if (start < data.Length)
        {
            result.Add(Encoding.UTF8.GetString(data, start, data.Length - start));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseKeyValueList(byte[] data)
    {
        IReadOnlyList<string> tokens = ParseStringList(data);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < tokens.Count; i += 2)
        {
            dict[tokens[i]] = tokens[i + 1];
        }

        return dict;
    }

    private static DateTimeOffset? ParseUnixNanoseconds(IReadOnlyDictionary<string, string> props, string key)
    {
        if (props.TryGetValue(key, out string? raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long nanos) && nanos > 0)
        {
            return DateTimeOffset.UnixEpoch.AddTicks(nanos / 100);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private readonly record struct AfcPacket(ulong PacketNumber, AfcOperation Operation, byte[] HeaderPayload, byte[] Body)
    {
        public byte[] GetCombinedPayload()
        {
            if (HeaderPayload.Length == 0)
            {
                return Body;
            }

            if (Body.Length == 0)
            {
                return HeaderPayload;
            }

            byte[] combined = new byte[HeaderPayload.Length + Body.Length];
            HeaderPayload.CopyTo(combined, 0);
            Body.CopyTo(combined, HeaderPayload.Length);
            return combined;
        }
    }
}
