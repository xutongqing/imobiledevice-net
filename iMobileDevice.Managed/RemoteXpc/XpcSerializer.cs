namespace iMobileDevice.Managed.RemoteXpc;

using System.Buffers.Binary;
using System.Text;

[Flags]
public enum XpcWrapperFlags : uint
{
    None = 0,
    AlwaysSet = 0x00000001,
    DataFlag = 0x00000100,
    InitStreamData = 0x00000200,
    WantingReply = 0x00010000,
    Reply = 0x00020000,
    FileTxStreamRequest = 0x00100000,
    FileTxStreamResponse = 0x00200000,
    InitHandshake = 0x00400000,
}

public sealed record XpcMessage(
    XpcWrapperFlags Flags,
    ulong MessageId,
    object? Body);

/// <summary>
/// Pure managed codec for Apple's RemoteXPC wire wrapper (<c>0x29B00B92</c>) and XPC binary object graph (<c>0x42133742</c>).
/// </summary>
public static class XpcSerializer
{
    public const uint WrapperMagic = 0x29B00B92;
    public const uint BodyMagic = 0x42133742;
    public const uint BodyVersion = 5;
    public const int WrapperHeaderSize = 24;

    private const uint TypeNull = 0x00001000;
    private const uint TypeBool = 0x00002000;
    private const uint TypeInt64 = 0x00003000;
    private const uint TypeUInt64 = 0x00004000;
    private const uint TypeDouble = 0x00005000;
    private const uint TypeDate = 0x00007000;
    private const uint TypeData = 0x00008000;
    private const uint TypeString = 0x00009000;
    private const uint TypeUuid = 0x0000A000;
    private const uint TypeArray = 0x0000E000;
    private const uint TypeDictionary = 0x0000F000;

    public static byte[] SerializeWrapper(XpcWrapperFlags flags, ulong messageId, object? body)
    {
        byte[] bodyBytes = body != null ? SerializeBody(body) : Array.Empty<byte>();
        XpcWrapperFlags effectiveFlags = flags | XpcWrapperFlags.AlwaysSet;

        byte[] result = new byte[WrapperHeaderSize + bodyBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), WrapperMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)effectiveFlags);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8, 8), (ulong)bodyBytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16, 8), messageId);
        if (bodyBytes.Length > 0)
        {
            bodyBytes.CopyTo(result.AsSpan(WrapperHeaderSize));
        }

        return result;
    }

    public static bool TryParseWrapper(ReadOnlySpan<byte> buffer, out XpcMessage message, out int bytesConsumed)
    {
        message = null!;
        bytesConsumed = 0;

        if (buffer.Length < WrapperHeaderSize)
        {
            return false;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
        if (magic != WrapperMagic)
        {
            throw new FormatException($"Invalid RemoteXPC wrapper magic: 0x{magic:X8}.");
        }

        var flags = (XpcWrapperFlags)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4));
        ulong bodyLen = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8));
        ulong msgId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(16, 8));

        if (bodyLen > 32 * 1024 * 1024)
        {
            throw new FormatException($"RemoteXPC body length too large: {bodyLen}.");
        }

        int totalLen = WrapperHeaderSize + (int)bodyLen;
        if (buffer.Length < totalLen)
        {
            return false;
        }

        object? body = bodyLen > 0 ? ParseBody(buffer.Slice(WrapperHeaderSize, (int)bodyLen)) : null;
        message = new XpcMessage(flags, msgId, body);
        bytesConsumed = totalLen;
        return true;
    }

    public static byte[] SerializeBody(object root)
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, BodyMagic);
        WriteUInt32(ms, BodyVersion);
        WriteObject(ms, root);
        return ms.ToArray();
    }

    public static object? ParseBody(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw new FormatException("XPC body too short.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (magic != BodyMagic)
        {
            throw new FormatException($"Invalid XPC body magic: 0x{magic:X8} (version {version}).");
        }

        int offset = 8;
        return ReadObject(data, ref offset);
    }

    private static object? ReadObject(ReadOnlySpan<byte> data, ref int offset)
    {
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        switch (type)
        {
            case TypeNull:
                return null;

            case TypeBool:
            {
                uint val = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                offset += 4;
                return val != 0;
            }

            case TypeInt64:
            {
                long val = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
                offset += 8;
                return val;
            }

            case TypeUInt64:
            {
                ulong val = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
                offset += 8;
                return val;
            }

            case TypeDouble:
            {
                double val = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8)));
                offset += 8;
                return val;
            }

            case TypeDate:
            {
                long nanos = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
                offset += 8;
                return DateTimeOffset.UnixEpoch.AddTicks(nanos / 100);
            }

            case TypeData:
            {
                int len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
                offset += 4;
                byte[] bytes = data.Slice(offset, len).ToArray();
                offset += Align4(len);
                return bytes;
            }

            case TypeString:
            {
                int len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
                offset += 4;
                int strLen = len > 0 && data[offset + len - 1] == 0 ? len - 1 : len;
                string s = Encoding.UTF8.GetString(data.Slice(offset, strLen));
                offset += Align4(len);
                return s;
            }

            case TypeUuid:
            {
                var guid = new Guid(data.Slice(offset, 16));
                offset += 16;
                return guid;
            }

            case TypeArray:
            {
                int byteLen = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
                offset += 4;
                if (byteLen < 4 || offset + byteLen > data.Length)
                {
                    throw new FormatException($"XPC array length {byteLen} out of bounds.");
                }

                int endOffset = offset + byteLen;
                int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
                offset += 4;

                int maxPossible = (endOffset - offset) / 4;
                if (count < 0 || count > maxPossible)
                {
                    throw new FormatException($"XPC array element count {count} exceeds remaining payload bytes ({endOffset - offset}).");
                }

                var list = new List<object?>(count);
                for (int i = 0; i < count; i++)
                {
                    list.Add(ReadObject(data, ref offset));
                }

                offset = endOffset;
                return list;
            }

            case TypeDictionary:
            {
                int byteLen = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
                offset += 4;
                if (byteLen < 4 || offset + byteLen > data.Length)
                {
                    throw new FormatException($"XPC dictionary length {byteLen} out of bounds.");
                }

                int endOffset = offset + byteLen;
                int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
                offset += 4;

                // Each entry requires at least 4 bytes for aligned key + 4 bytes for value type header
                int maxPossible = (endOffset - offset) / 8;
                if (count < 0 || count > maxPossible)
                {
                    throw new FormatException($"XPC dictionary entry count {count} exceeds remaining payload bytes ({endOffset - offset}).");
                }

                var dict = new Dictionary<string, object?>(count, StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    int nulIdx = data.Slice(offset, endOffset - offset).IndexOf((byte)0);
                    if (nulIdx < 0)
                    {
                        throw new FormatException("Unterminated key in XPC dictionary.");
                    }

                    string key = Encoding.UTF8.GetString(data.Slice(offset, nulIdx));
                    offset += Align4(nulIdx + 1);
                    dict[key] = ReadObject(data, ref offset);
                }

                offset = endOffset;
                return dict;
            }

            default:
                throw new FormatException($"Unsupported XPC object type: 0x{type:X8} at offset {offset - 4}.");
        }
    }

    private static void WriteObject(MemoryStream ms, object? value)
    {
        switch (value)
        {
            case null:
                WriteUInt32(ms, TypeNull);
                break;

            case bool b:
                WriteUInt32(ms, TypeBool);
                WriteUInt32(ms, b ? 1u : 0u);
                break;

            case int i:
                WriteUInt32(ms, TypeInt64);
                WriteInt64(ms, i);
                break;

            case long l:
                WriteUInt32(ms, TypeInt64);
                WriteInt64(ms, l);
                break;

            case ulong ul:
                WriteUInt32(ms, TypeUInt64);
                WriteUInt64(ms, ul);
                break;

            case double d:
                WriteUInt32(ms, TypeDouble);
                WriteInt64(ms, BitConverter.DoubleToInt64Bits(d));
                break;

            case DateTimeOffset dt:
                WriteUInt32(ms, TypeDate);
                WriteInt64(ms, (dt.ToUniversalTime() - DateTimeOffset.UnixEpoch).Ticks * 100);
                break;

            case byte[] bytes:
                WriteUInt32(ms, TypeData);
                WriteUInt32(ms, (uint)bytes.Length);
                ms.Write(bytes);
                WritePad4(ms, bytes.Length);
                break;

            case string s:
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(s);
                int lenWithNull = utf8.Length + 1;
                WriteUInt32(ms, TypeString);
                WriteUInt32(ms, (uint)lenWithNull);
                ms.Write(utf8);
                ms.WriteByte(0);
                WritePad4(ms, lenWithNull);
                break;
            }

            case Guid g:
                WriteUInt32(ms, TypeUuid);
                ms.Write(g.ToByteArray());
                break;

            case IList<object?> list:
            {
                using var inner = new MemoryStream();
                WriteUInt32(inner, (uint)list.Count);
                foreach (var item in list)
                {
                    WriteObject(inner, item);
                }

                byte[] payload = inner.ToArray();
                WriteUInt32(ms, TypeArray);
                WriteUInt32(ms, (uint)payload.Length);
                ms.Write(payload);
                break;
            }

            case IDictionary<string, object?> dict:
            {
                using var inner = new MemoryStream();
                WriteUInt32(inner, (uint)dict.Count);
                foreach (var kv in dict)
                {
                    byte[] keyBytes = Encoding.UTF8.GetBytes(kv.Key);
                    inner.Write(keyBytes);
                    inner.WriteByte(0);
                    WritePad4(inner, keyBytes.Length + 1);
                    WriteObject(inner, kv.Value);
                }

                byte[] payload = inner.ToArray();
                WriteUInt32(ms, TypeDictionary);
                WriteUInt32(ms, (uint)payload.Length);
                ms.Write(payload);
                break;
            }

            default:
                throw new NotSupportedException($"Unsupported XPC value type: {value.GetType().Name}");
        }
    }

    private static int Align4(int len) => (len + 3) & ~3;

    private static void WritePad4(MemoryStream ms, int len)
    {
        int pad = Align4(len) - len;
        for (int i = 0; i < pad; i++)
        {
            ms.WriteByte(0);
        }
    }

    private static void WriteUInt32(MemoryStream ms, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteInt64(MemoryStream ms, long value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteUInt64(MemoryStream ms, ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        ms.Write(buf);
    }
}
