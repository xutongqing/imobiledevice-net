namespace iMobileDevice.Managed.Services;

using System.Buffers.Binary;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Shared 4-byte big-endian length-prefixed plist framing used by Lockdown-hosted property list services.
/// </summary>
internal sealed class PlistServiceWire(Stream stream, PlistFormat sendFormat = PlistFormat.Xml)
{
    private const uint MaxPayloadLength = 64U * 1024 * 1024;

    public async Task SendAsync(PlistNode value, CancellationToken cancellationToken)
    {
        byte[] body = PlistSerializer.Serialize(value, sendFormat);
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)body.Length);
        body.CopyTo(frame.AsSpan(4));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlistNode> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0 || length > MaxPayloadLength)
        {
            throw new InvalidDataException($"Invalid plist service frame length: {length}.");
        }

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return PlistSerializer.Parse(body);
    }

    public async Task<PlistDictionary> ExchangeDictionaryAsync(PlistDictionary request, CancellationToken cancellationToken)
    {
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        PlistNode node = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (node is not PlistDictionary dict)
        {
            throw new InvalidDataException($"Expected dictionary reply from service, received {node.Kind}.");
        }

        return dict;
    }
}
