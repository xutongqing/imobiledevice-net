namespace iMobileDevice.Managed.Apps;

using System.Buffers.Binary;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Frames binary plists over a service stream the way the installation and house arrest services
/// expect: a big-endian 32-bit length followed by the plist body.
/// </summary>
internal sealed class AppsWire(Stream stream)
{
    private const uint MaxReplyLength = 128U * 1024 * 1024;

    public async Task SendAsync(PlistNode value, CancellationToken token)
    {
        byte[] body = PlistSerializer.WriteBinary(value);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)body.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public async Task<PlistNode> ReceiveAsync(CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length > MaxReplyLength)
        {
            throw new InvalidDataException($"A service reply of {length} bytes exceeds the size limit.");
        }

        var body = new byte[(int)length];
        await stream.ReadExactlyAsync(body, token).ConfigureAwait(false);
        return PlistSerializer.Parse(body);
    }
}
