namespace iMobileDevice.Managed.Backup;

using System.Buffers.Binary;
using iMobileDevice.Managed.Plist;

internal sealed class BackupWire(Stream stream)
{
    public Stream Stream { get; } = stream;

    public async Task<uint> ReadLengthAsync(CancellationToken token)
    {
        byte[] header = new byte[4];
        await Stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt32BigEndian(header);
    }

    public async Task<byte[]> ReadBytesAsync(uint size, int limit, CancellationToken token)
    {
        if (size > limit) throw new InvalidDataException("Backup message exceeds the size limit.");
        var bytes = new byte[(int)size];
        await Stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    public async Task<PlistNode> ReceiveAsync(CancellationToken token)
    {
        uint size = await ReadLengthAsync(token).ConfigureAwait(false);
        return PlistSerializer.Parse(await ReadBytesAsync(size, 128 * 1024 * 1024, token).ConfigureAwait(false));
    }

    public async Task WriteBlockAsync(ReadOnlyMemory<byte> bytes, byte? code, CancellationToken token)
    {
        var header = new byte[code.HasValue ? 5 : 4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)bytes.Length + (code.HasValue ? 1u : 0u));
        if (code.HasValue) header[4] = code.Value;
        await Stream.WriteAsync(header, token).ConfigureAwait(false);
        if (!bytes.IsEmpty) await Stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }

    public async Task SendAsync(PlistNode value, CancellationToken token)
    {
        await WriteBlockAsync(PlistSerializer.WriteBinary(value), null, token).ConfigureAwait(false);
        await Stream.FlushAsync(token).ConfigureAwait(false);
    }
}
