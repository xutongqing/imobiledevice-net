namespace iMobileDevice.Managed.Plist;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;

public enum PlistFormat
{
    Xml,
    Binary,
}

/// <summary>
/// Pure managed Apple Property List serializer supporting XML 1.0 and binary (<c>bplist00</c>) formats.
/// </summary>
public static class PlistSerializer
{
    private static ReadOnlySpan<byte> BinaryMagic => "bplist00"u8;

    public static bool IsBinary(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data.Slice(0, 8).SequenceEqual(BinaryMagic);

    public static PlistNode Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new FormatException("Plist payload is empty.");
        }

        return IsBinary(data) ? ParseBinary(data) : ParseXml(data);
    }

    public static byte[] Serialize(PlistNode node, PlistFormat format = PlistFormat.Xml) =>
        format == PlistFormat.Binary ? WriteBinary(node) : WriteXml(node);

    #region XML Plist

    public static PlistNode ParseXml(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = true,
        };

        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "plist")
            {
                if (reader.IsEmptyElement)
                {
                    throw new FormatException("XML plist root element is empty.");
                }

                reader.ReadStartElement("plist");
                if (reader.NodeType != XmlNodeType.Element)
                {
                    throw new FormatException("XML plist has no root value element.");
                }

                return ReadXmlNode(reader);
            }
        }

        throw new FormatException("XML document does not contain a <plist> root element.");
    }

    private static PlistNode ReadXmlNode(XmlReader reader)
    {
        string name = reader.Name;
        switch (name)
        {
            case "true":
                reader.Skip();
                return PlistBoolean.True;

            case "false":
                reader.Skip();
                return PlistBoolean.False;

            case "integer":
            {
                string text = reader.ReadElementContentAsString().Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return new PlistInteger(ulong.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed))
                {
                    return new PlistInteger(signed);
                }

                return new PlistInteger(ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
            }

            case "real":
            {
                string text = reader.ReadElementContentAsString().Trim();
                if (string.Equals(text, "nan", StringComparison.OrdinalIgnoreCase))
                {
                    return new PlistReal(double.NaN);
                }
                if (string.Equals(text, "+infinity", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "inf", StringComparison.OrdinalIgnoreCase))
                {
                    return new PlistReal(double.PositiveInfinity);
                }
                if (string.Equals(text, "-infinity", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "-inf", StringComparison.OrdinalIgnoreCase))
                {
                    return new PlistReal(double.NegativeInfinity);
                }

                return new PlistReal(double.Parse(text, CultureInfo.InvariantCulture));
            }

            case "string":
                return new PlistString(reader.ReadElementContentAsString());

            case "data":
            {
                string b64 = reader.ReadElementContentAsString();
                if (string.IsNullOrWhiteSpace(b64))
                {
                    return new PlistData(Array.Empty<byte>());
                }

                string cleaned = new string(b64.Where(c => !char.IsWhiteSpace(c)).ToArray());
                return new PlistData(Convert.FromBase64String(cleaned));
            }

            case "date":
            {
                string text = reader.ReadElementContentAsString().Trim();
                var dt = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                return new PlistDate(dt);
            }

            case "array":
            {
                var array = new PlistArray();
                if (reader.IsEmptyElement)
                {
                    reader.Skip();
                    return array;
                }

                reader.ReadStartElement("array");
                while (reader.NodeType != XmlNodeType.EndElement)
                {
                    array.Add(ReadXmlNode(reader));
                }

                reader.ReadEndElement();
                return array;
            }

            case "dict":
            {
                var dict = new PlistDictionary();
                if (reader.IsEmptyElement)
                {
                    reader.Skip();
                    return dict;
                }

                reader.ReadStartElement("dict");
                while (reader.NodeType != XmlNodeType.EndElement)
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "key")
                    {
                        throw new FormatException($"Expected <key> in <dict>, found <{reader.Name}>.");
                    }

                    string key = reader.ReadElementContentAsString();
                    PlistNode value = ReadXmlNode(reader);
                    dict[key] = value;
                }

                reader.ReadEndElement();
                return dict;
            }

            default:
                throw new FormatException($"Unsupported XML plist element <{name}>.");
        }
    }

    public static byte[] WriteXml(PlistNode node)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "\t",
            NewLineChars = "\n",
            OmitXmlDeclaration = false,
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteDocType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
            writer.WriteStartElement("plist");
            writer.WriteAttributeString("version", "1.0");
            WriteXmlNode(writer, node);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteXmlNode(XmlWriter writer, PlistNode node)
    {
        switch (node)
        {
            case PlistBoolean b:
                writer.WriteStartElement(b.Value ? "true" : "false");
                writer.WriteEndElement();
                break;

            case PlistInteger i:
                writer.WriteElementString("integer", i.IsUnsigned
                    ? i.UInt64Value.ToString(CultureInfo.InvariantCulture)
                    : i.Int64Value.ToString(CultureInfo.InvariantCulture));
                break;

            case PlistReal r:
                writer.WriteElementString("real", r.Value.ToString("R", CultureInfo.InvariantCulture));
                break;

            case PlistString s:
                writer.WriteElementString("string", s.Value);
                break;

            case PlistData d:
                writer.WriteElementString("data", Convert.ToBase64String(d.Value));
                break;

            case PlistDate dt:
                writer.WriteElementString("date", dt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                break;

            case PlistUid u:
                writer.WriteStartElement("dict");
                writer.WriteElementString("key", "CF$UID");
                writer.WriteElementString("integer", u.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                break;

            case PlistArray arr:
                writer.WriteStartElement("array");
                foreach (var item in arr)
                {
                    WriteXmlNode(writer, item);
                }
                writer.WriteEndElement();
                break;

            case PlistDictionary dict:
                writer.WriteStartElement("dict");
                foreach (var kv in dict)
                {
                    writer.WriteElementString("key", kv.Key);
                    WriteXmlNode(writer, kv.Value);
                }
                writer.WriteEndElement();
                break;

            default:
                throw new NotSupportedException($"Unsupported PlistNode type: {node.GetType().Name}");
        }
    }

    #endregion

    #region Binary Plist (bplist00)

    public static PlistNode ParseBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length < 40 || !IsBinary(data))
        {
            throw new FormatException("Invalid bplist00 header or truncated payload.");
        }

        ReadOnlySpan<byte> trailer = data.Slice(data.Length - 32, 32);
        byte offsetIntSize = trailer[6];
        byte objectRefSize = trailer[7];
        ulong numObjects = BinaryPrimitives.ReadUInt64BigEndian(trailer.Slice(8, 8));
        ulong topObject = BinaryPrimitives.ReadUInt64BigEndian(trailer.Slice(16, 8));
        ulong offsetTableOffset = BinaryPrimitives.ReadUInt64BigEndian(trailer.Slice(24, 8));

        if (offsetIntSize is 0 or > 8 || objectRefSize is 0 or > 8)
        {
            throw new FormatException($"Invalid bplist00 integer sizes: offset={offsetIntSize}, ref={objectRefSize}.");
        }

        if (numObjects > int.MaxValue || topObject >= numObjects)
        {
            throw new FormatException("Invalid bplist00 object count or top object index.");
        }

        int objCount = (int)numObjects;
        ulong offsetTableEnd = offsetTableOffset + (ulong)objCount * offsetIntSize;
        if (offsetTableEnd > (ulong)(data.Length - 32))
        {
            throw new FormatException("bplist00 offset table exceeds payload bounds.");
        }

        var offsets = new ulong[objCount];
        int pos = (int)offsetTableOffset;
        for (int i = 0; i < objCount; i++)
        {
            offsets[i] = ReadSizedUInt(data.Slice(pos, offsetIntSize));
            pos += offsetIntSize;
        }

        return ReadBinaryObject(data, offsets, objectRefSize, (int)topObject, 0);
    }

    private static PlistNode ReadBinaryObject(
        ReadOnlySpan<byte> data,
        ulong[] offsets,
        byte objectRefSize,
        int objectIndex,
        int depth)
    {
        if (depth > 128)
        {
            throw new FormatException("bplist00 nesting depth exceeded.");
        }

        if ((uint)objectIndex >= (uint)offsets.Length)
        {
            throw new FormatException($"bplist00 object reference {objectIndex} out of range.");
        }

        int offset = checked((int)offsets[objectIndex]);
        byte marker = data[offset++];
        int high = (marker & 0xF0) >> 4;
        int low = marker & 0x0F;

        switch (high)
        {
            case 0x0:
                return low switch
                {
                    0x08 => PlistBoolean.False,
                    0x09 => PlistBoolean.True,
                    0x00 => new PlistString(string.Empty),
                    _ => throw new FormatException($"Unsupported bplist00 singleton marker 0x{marker:X2}."),
                };

            case 0x1:
            {
                int byteCount = 1 << low;
                if (byteCount == 16)
                {
                    // Apple encodes unsigned 64-bit integers (> Int64.MaxValue) as 16-byte big-endian
                    ulong upper = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));
                    ulong lower = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset + 8, 8));
                    if (upper == 0)
                    {
                        return new PlistInteger(lower);
                    }

                    return new PlistInteger(unchecked((long)lower));
                }

                if (byteCount == 8)
                {
                    return new PlistInteger(BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8)));
                }

                ulong uval = ReadSizedUInt(data.Slice(offset, byteCount));
                return new PlistInteger(checked((long)uval));
            }

            case 0x2:
            {
                int byteCount = 1 << low;
                return byteCount switch
                {
                    4 => new PlistReal(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4)))),
                    8 => new PlistReal(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8)))),
                    _ => throw new FormatException($"Unsupported bplist00 real size {byteCount}."),
                };
            }

            case 0x3:
            {
                double seconds = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8)));
                return new PlistDate(PlistDate.AppleEpoch.AddSeconds(seconds));
            }

            case 0x4:
            {
                int length = ReadBinaryLength(data, ref offset, low);
                return new PlistData(data.Slice(offset, length).ToArray());
            }

            case 0x5:
            {
                int length = ReadBinaryLength(data, ref offset, low);
                return new PlistString(Encoding.ASCII.GetString(data.Slice(offset, length)));
            }

            case 0x6:
            {
                int charCount = ReadBinaryLength(data, ref offset, low);
                return new PlistString(Encoding.BigEndianUnicode.GetString(data.Slice(offset, charCount * 2)));
            }

            case 0x8:
            {
                int byteCount = low + 1;
                return new PlistUid(ReadSizedUInt(data.Slice(offset, byteCount)));
            }

            case 0xA:
            {
                int count = ReadBinaryLength(data, ref offset, low);
                var array = new PlistArray();
                for (int i = 0; i < count; i++)
                {
                    int childRef = checked((int)ReadSizedUInt(data.Slice(offset + i * objectRefSize, objectRefSize)));
                    array.Add(ReadBinaryObject(data, offsets, objectRefSize, childRef, depth + 1));
                }

                return array;
            }

            case 0xD:
            {
                int count = ReadBinaryLength(data, ref offset, low);
                var dict = new PlistDictionary();
                int keysStart = offset;
                int valsStart = offset + count * objectRefSize;
                for (int i = 0; i < count; i++)
                {
                    int keyRef = checked((int)ReadSizedUInt(data.Slice(keysStart + i * objectRefSize, objectRefSize)));
                    int valRef = checked((int)ReadSizedUInt(data.Slice(valsStart + i * objectRefSize, objectRefSize)));
                    string key = ReadBinaryObject(data, offsets, objectRefSize, keyRef, depth + 1).AsString();
                    PlistNode val = ReadBinaryObject(data, offsets, objectRefSize, valRef, depth + 1);
                    dict[key] = val;
                }

                return dict;
            }

            default:
                throw new FormatException($"Unsupported bplist00 marker 0x{marker:X2}.");
        }
    }

    private static int ReadBinaryLength(ReadOnlySpan<byte> data, ref int offset, int low)
    {
        if (low != 0x0F)
        {
            return low;
        }

        byte intMarker = data[offset++];
        if ((intMarker & 0xF0) != 0x10)
        {
            throw new FormatException($"Expected integer marker for bplist00 extended length, got 0x{intMarker:X2}.");
        }

        int byteCount = 1 << (intMarker & 0x0F);
        ulong len = ReadSizedUInt(data.Slice(offset, byteCount));
        offset += byteCount;
        return checked((int)len);
    }

    private static ulong ReadSizedUInt(ReadOnlySpan<byte> bytes)
    {
        ulong result = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            result = (result << 8) | bytes[i];
        }

        return result;
    }

    public static byte[] WriteBinary(PlistNode root)
    {
        var objects = new List<PlistNode>();
        var indexMap = new Dictionary<PlistNode, int>(ReferenceEqualityComparer.Instance);
        var stringPool = new Dictionary<string, int>(StringComparer.Ordinal);

        int Collect(PlistNode node)
        {
            if (node is PlistString ps && stringPool.TryGetValue(ps.Value, out int existingStr))
            {
                indexMap[node] = existingStr;
                return existingStr;
            }

            if (indexMap.TryGetValue(node, out int existing))
            {
                return existing;
            }

            int idx = objects.Count;
            objects.Add(node);
            indexMap[node] = idx;
            if (node is PlistString s)
            {
                stringPool[s.Value] = idx;
            }

            switch (node)
            {
                case PlistArray arr:
                    foreach (var item in arr)
                    {
                        Collect(item);
                    }
                    break;

                case PlistDictionary dict:
                    foreach (var kv in dict)
                    {
                        Collect(new PlistString(kv.Key));
                        Collect(kv.Value);
                    }
                    break;
            }

            return idx;
        }

        int topIndex = Collect(root);
        byte refSize = objects.Count <= 0xFF ? (byte)1 : objects.Count <= 0xFFFF ? (byte)2 : (byte)4;

        using var ms = new MemoryStream();
        ms.Write(BinaryMagic);

        var offsets = new ulong[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i] = (ulong)ms.Position;
            WriteBinaryObject(ms, objects[i], indexMap, stringPool, refSize);
        }

        ulong offsetTableOffset = (ulong)ms.Position;
        byte offsetIntSize = offsetTableOffset <= 0xFF ? (byte)1
            : offsetTableOffset <= 0xFFFF ? (byte)2
            : offsetTableOffset <= 0xFFFFFFFF ? (byte)4
            : (byte)8;

        for (int i = 0; i < offsets.Length; i++)
        {
            WriteSizedUInt(ms, offsets[i], offsetIntSize);
        }

        Span<byte> trailer = stackalloc byte[32];
        trailer.Clear();
        trailer[6] = offsetIntSize;
        trailer[7] = refSize;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.Slice(8, 8), (ulong)objects.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.Slice(16, 8), (ulong)topIndex);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.Slice(24, 8), offsetTableOffset);
        ms.Write(trailer);

        return ms.ToArray();
    }

    private static void WriteBinaryObject(
        MemoryStream ms,
        PlistNode node,
        Dictionary<PlistNode, int> indexMap,
        Dictionary<string, int> stringPool,
        byte refSize)
    {
        switch (node)
        {
            case PlistBoolean b:
                ms.WriteByte(b.Value ? (byte)0x09 : (byte)0x08);
                break;

            case PlistInteger i:
                if (i.IsUnsigned)
                {
                    ms.WriteByte(0x14); // 16-byte integer for unsigned > Int64.MaxValue
                    Span<byte> buf16 = stackalloc byte[16];
                    buf16.Slice(0, 8).Clear();
                    BinaryPrimitives.WriteUInt64BigEndian(buf16.Slice(8, 8), i.UInt64Value);
                    ms.Write(buf16);
                }
                else if (i.Int64Value < 0)
                {
                    ms.WriteByte(0x13); // 8-byte signed
                    Span<byte> buf8 = stackalloc byte[8];
                    BinaryPrimitives.WriteInt64BigEndian(buf8, i.Int64Value);
                    ms.Write(buf8);
                }
                else if (i.Int64Value <= 0xFF)
                {
                    ms.WriteByte(0x10);
                    ms.WriteByte((byte)i.Int64Value);
                }
                else if (i.Int64Value <= 0xFFFF)
                {
                    ms.WriteByte(0x11);
                    Span<byte> buf2 = stackalloc byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(buf2, (ushort)i.Int64Value);
                    ms.Write(buf2);
                }
                else if (i.Int64Value <= 0xFFFFFFFF)
                {
                    ms.WriteByte(0x12);
                    Span<byte> buf4 = stackalloc byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(buf4, (uint)i.Int64Value);
                    ms.Write(buf4);
                }
                else
                {
                    ms.WriteByte(0x13);
                    Span<byte> buf8 = stackalloc byte[8];
                    BinaryPrimitives.WriteInt64BigEndian(buf8, i.Int64Value);
                    ms.Write(buf8);
                }
                break;

            case PlistReal r:
            {
                ms.WriteByte(0x23);
                Span<byte> buf8 = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buf8, BitConverter.DoubleToInt64Bits(r.Value));
                ms.Write(buf8);
                break;
            }

            case PlistDate dt:
            {
                ms.WriteByte(0x33);
                double secs = (dt.Value.ToUniversalTime() - PlistDate.AppleEpoch).TotalSeconds;
                Span<byte> buf8 = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buf8, BitConverter.DoubleToInt64Bits(secs));
                ms.Write(buf8);
                break;
            }

            case PlistData d:
                WriteMarkerAndLength(ms, 0x40, d.Value.Length);
                ms.Write(d.Value);
                break;

            case PlistString s:
                if (IsAscii(s.Value))
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(s.Value);
                    WriteMarkerAndLength(ms, 0x50, bytes.Length);
                    ms.Write(bytes);
                }
                else
                {
                    byte[] bytes = Encoding.BigEndianUnicode.GetBytes(s.Value);
                    WriteMarkerAndLength(ms, 0x60, bytes.Length / 2);
                    ms.Write(bytes);
                }
                break;

            case PlistUid u:
            {
                int byteLen = u.Value <= 0xFF ? 1 : u.Value <= 0xFFFF ? 2 : u.Value <= 0xFFFFFFFF ? 4 : 8;
                ms.WriteByte((byte)(0x80 | (byteLen - 1)));
                WriteSizedUInt(ms, u.Value, byteLen);
                break;
            }

            case PlistArray arr:
                WriteMarkerAndLength(ms, 0xA0, arr.Count);
                foreach (var item in arr)
                {
                    WriteSizedUInt(ms, (ulong)indexMap[item], refSize);
                }
                break;

            case PlistDictionary dict:
                WriteMarkerAndLength(ms, 0xD0, dict.Count);
                foreach (var kv in dict)
                {
                    WriteSizedUInt(ms, (ulong)stringPool[kv.Key], refSize);
                }
                foreach (var kv in dict)
                {
                    WriteSizedUInt(ms, (ulong)indexMap[kv.Value], refSize);
                }
                break;

            default:
                throw new NotSupportedException($"Unsupported PlistNode type: {node.GetType().Name}");
        }
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteMarkerAndLength(MemoryStream ms, byte highMarker, int length)
    {
        if (length < 15)
        {
            ms.WriteByte((byte)(highMarker | length));
            return;
        }

        ms.WriteByte((byte)(highMarker | 0x0F));
        if (length <= 0xFF)
        {
            ms.WriteByte(0x10);
            ms.WriteByte((byte)length);
        }
        else if (length <= 0xFFFF)
        {
            ms.WriteByte(0x11);
            Span<byte> buf2 = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf2, (ushort)length);
            ms.Write(buf2);
        }
        else
        {
            ms.WriteByte(0x12);
            Span<byte> buf4 = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf4, (uint)length);
            ms.Write(buf4);
        }
    }

    private static void WriteSizedUInt(MemoryStream ms, ulong value, int size)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buf, value);
        ms.Write(buf.Slice(8 - size, size));
    }

    #endregion
}
