namespace iMobileDevice.Managed.Bonjour;

using System.Buffers.Binary;
using System.Net;
using System.Text;

internal enum DnsRecordType : ushort
{
    A = 1,
    PTR = 12,
    TXT = 16,
    AAAA = 28,
    SRV = 33,
    ANY = 255,
}

internal sealed class DnsWireName : IEquatable<DnsWireName>
{
    public static DnsWireName Root { get; } = new([], ".");

    public DnsWireName(IReadOnlyList<string> labels)
        : this(labels, labels is { Count: 0 } ? "." : string.Concat(string.Join('.', labels ?? throw new ArgumentNullException(nameof(labels))), "."))
    {
    }

    public DnsWireName(IReadOnlyList<string> labels, string displayName)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(displayName);
        Labels = Array.AsReadOnly(labels.ToArray());
        DisplayName = displayName;
    }

    public IReadOnlyList<string> Labels { get; }

    public string DisplayName { get; }

    public bool IsRoot => Labels.Count == 0;

    public string StripTrailingDot() => IsRoot ? string.Empty : string.Join('.', Labels);

    public static DnsWireName Parse(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string trimmed = name.Trim();
        if (trimmed == ".")
        {
            return Root;
        }

        string withoutTrailingDot = trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;
        if (withoutTrailingDot.Length == 0)
        {
            throw new ArgumentException("DNS name cannot be empty or consist only of multiple dots.", nameof(name));
        }

        string[] labels = withoutTrailingDot.Split('.');
        foreach (string label in labels)
        {
            if (label.Length == 0)
            {
                throw new ArgumentException(
                    $"DNS label '{label}' must be between 1 and {DnsWireCodec.MaxLabelBytes} UTF-8 bytes.",
                    nameof(name));
            }
        }

        return new DnsWireName(labels);
    }

    public bool Equals(DnsWireName? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Labels.Count != other.Labels.Count)
        {
            return false;
        }

        for (int i = 0; i < Labels.Count; i++)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(Labels[i], other.Labels[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is DnsWireName other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < Labels.Count; i++)
        {
            hash.Add(Labels[i], StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => DisplayName;
}

internal readonly record struct DnsQuestion(DnsWireName Name, DnsRecordType Type, bool UnicastResponse = false);

internal sealed record DnsResourceRecord(
    DnsWireName Name,
    DnsRecordType Type,
    ushort Class,
    uint Ttl,
    DnsWireName? TargetName = null,
    ushort? Priority = null,
    ushort? Weight = null,
    ushort? Port = null,
    IPAddress? Address = null,
    IReadOnlyDictionary<string, string?>? TxtRecords = null);

internal sealed record DnsMessage(
    ushort TransactionId,
    ushort Flags,
    IReadOnlyList<DnsQuestion> Questions,
    IReadOnlyList<DnsResourceRecord> Answers,
    IReadOnlyList<DnsResourceRecord> Authorities,
    IReadOnlyList<DnsResourceRecord> Additional)
{
    public bool IsResponse => (Flags & 0x8000) != 0;
    public int ResponseCode => Flags & 0x000F;
}

internal static class DnsWireCodec
{
    internal const ushort ClassIn = 1;
    internal const ushort ClassMask = 0x7FFF;
    internal const ushort UnicastResponseBit = 0x8000;
    internal const int HeaderSize = 12;
    internal const int MaxLabelBytes = 63;
    internal const int MaxWireNameBytes = 255;
    internal const int MaxCompressionHops = 128;

    public static byte[] EncodePtrQuery(string serviceType, bool unicastResponse = false)
    {
        string normalized = BonjourServiceTypes.Normalize(serviceType);
        return EncodeQuestions([new DnsQuestion(DnsWireName.Parse(normalized), DnsRecordType.PTR, unicastResponse)]);
    }

    public static byte[] EncodeQuestions(IReadOnlyList<DnsQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0 || questions.Count > ushort.MaxValue)
        {
            throw new ArgumentException("Question count must be between 1 and 65535.", nameof(questions));
        }

        var buffer = new List<byte>(64);
        // Header: TXID=0, Flags=0, QDCOUNT=questions.Count, AN=0, NS=0, AR=0
        buffer.Add(0);
        buffer.Add(0);
        buffer.Add(0);
        buffer.Add(0);
        WriteUInt16BigEndian(buffer, (ushort)questions.Count);
        for (int i = 0; i < 6; i++)
        {
            buffer.Add(0);
        }

        foreach (DnsQuestion question in questions)
        {
            WriteEncodedName(buffer, question.Name);
            WriteUInt16BigEndian(buffer, (ushort)question.Type);
            ushort qclass = question.UnicastResponse ? (ushort)(ClassIn | UnicastResponseBit) : ClassIn;
            WriteUInt16BigEndian(buffer, qclass);
        }

        return buffer.ToArray();
    }

    public static byte[] EncodeName(string name)
        => EncodeName(DnsWireName.Parse(name));

    public static byte[] EncodeName(DnsWireName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var buffer = new List<byte>(name.DisplayName.Length + 2);
        WriteEncodedName(buffer, name);
        return buffer.ToArray();
    }

    public static bool CanEncodeName(DnsWireName? name)
    {
        if (name is null)
        {
            return false;
        }

        if (name.Labels.Count == 0)
        {
            return true;
        }

        int totalBytes = 1;
        foreach (string label in name.Labels)
        {
            int byteCount = Encoding.UTF8.GetByteCount(label);
            if (byteCount == 0 || byteCount > MaxLabelBytes)
            {
                return false;
            }

            totalBytes += 1 + byteCount;
        }

        return totalBytes <= MaxWireNameBytes;
    }

    public static bool TryDecodeMessage(ReadOnlySpan<byte> datagram, out DnsMessage message)
    {
        message = default!;
        if (datagram.Length < HeaderSize)
        {
            return false;
        }

        ushort txId = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(0, 2));
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(2, 2));
        ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(4, 2));
        ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(6, 2));
        ushort nsCount = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(8, 2));
        ushort arCount = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(10, 2));

        int offset = HeaderSize;
        var questions = qdCount > 0 ? new List<DnsQuestion>(Math.Min((int)qdCount, 32)) : [];
        for (int i = 0; i < qdCount; i++)
        {
            if (!TryDecodeName(datagram, ref offset, out DnsWireName qname))
            {
                return false;
            }

            if (offset + 4 > datagram.Length)
            {
                return false;
            }

            ushort qtype = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(offset, 2));
            ushort rawQclass = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(offset + 2, 2));
            offset += 4;

            bool unicast = (rawQclass & UnicastResponseBit) != 0;
            questions.Add(new DnsQuestion(qname, (DnsRecordType)qtype, unicast));
        }

        if (!TryDecodeSection(datagram, ref offset, anCount, out List<DnsResourceRecord> answers) ||
            !TryDecodeSection(datagram, ref offset, nsCount, out List<DnsResourceRecord> authorities) ||
            !TryDecodeSection(datagram, ref offset, arCount, out List<DnsResourceRecord> additional))
        {
            return false;
        }

        message = new DnsMessage(txId, flags, questions, answers, authorities, additional);
        return true;
    }

    public static bool TryDecodeName(ReadOnlySpan<byte> datagram, ref int offset, out DnsWireName name)
        => TryDecodeName(datagram, ref offset, datagram.Length, out name);

    public static bool TryParseTxtRecords(ReadOnlySpan<byte> rdata, out Dictionary<string, string?> records)
    {
        records = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (rdata.Length == 0 || (rdata.Length == 1 && rdata[0] == 0))
        {
            return true;
        }

        int pos = 0;
        while (pos < rdata.Length)
        {
            int length = rdata[pos++];
            if (length == 0)
            {
                continue;
            }

            if (pos + length > rdata.Length)
            {
                return false;
            }

            string entry = Encoding.UTF8.GetString(rdata.Slice(pos, length));
            pos += length;

            int equalsIndex = entry.IndexOf('=');
            if (equalsIndex == 0)
            {
                // RFC 6763 section 6.4: attributes with empty keys are invalid/ignored.
                continue;
            }

            if (equalsIndex > 0)
            {
                string key = entry[..equalsIndex];
                string value = entry[(equalsIndex + 1)..];
                records.TryAdd(key, value);
            }
            else
            {
                records.TryAdd(entry, null);
            }
        }

        return true;
    }

    private static bool TryDecodeSection(
        ReadOnlySpan<byte> datagram,
        ref int offset,
        ushort count,
        out List<DnsResourceRecord> records)
    {
        records = count > 0 ? new List<DnsResourceRecord>(Math.Min((int)count, 32)) : [];
        for (int i = 0; i < count; i++)
        {
            if (!TryDecodeName(datagram, ref offset, out DnsWireName rrName))
            {
                return false;
            }

            if (offset + 10 > datagram.Length)
            {
                return false;
            }

            ushort rtypeValue = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(offset, 2));
            ushort rawClass = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(offset + 2, 2));
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(offset + 4, 4));
            ushort rdLength = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(offset + 8, 2));
            offset += 10;

            if (offset + rdLength > datagram.Length)
            {
                return false;
            }

            int rdataStart = offset;
            int rdataEnd = offset + rdLength;
            ReadOnlySpan<byte> rdata = datagram.Slice(rdataStart, rdLength);
            offset = rdataEnd;

            ushort rclass = (ushort)(rawClass & ClassMask);
            if (rclass != ClassIn)
            {
                // Ignore non-IN records (e.g., EDNS0 OPT) cleanly without failing the packet.
                continue;
            }

            var rtype = (DnsRecordType)rtypeValue;
            switch (rtype)
            {
                case DnsRecordType.PTR:
                {
                    int ptrOffset = rdataStart;
                    if (!TryDecodeName(datagram, ref ptrOffset, rdataEnd, out DnsWireName ptrTarget))
                    {
                        return false;
                    }

                    records.Add(new DnsResourceRecord(rrName, rtype, rclass, ttl, TargetName: ptrTarget));
                    break;
                }

                case DnsRecordType.SRV:
                {
                    if (rdLength < 6)
                    {
                        return false;
                    }

                    ushort priority = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(0, 2));
                    ushort weight = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(2, 2));
                    ushort port = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(4, 2));

                    int targetOffset = rdataStart + 6;
                    if (!TryDecodeName(datagram, ref targetOffset, rdataEnd, out DnsWireName target))
                    {
                        return false;
                    }

                    records.Add(new DnsResourceRecord(
                        rrName,
                        rtype,
                        rclass,
                        ttl,
                        TargetName: target,
                        Priority: priority,
                        Weight: weight,
                        Port: port));
                    break;
                }

                case DnsRecordType.TXT:
                {
                    if (!TryParseTxtRecords(rdata, out Dictionary<string, string?> txt))
                    {
                        return false;
                    }

                    records.Add(new DnsResourceRecord(rrName, rtype, rclass, ttl, TxtRecords: txt));
                    break;
                }

                case DnsRecordType.A:
                {
                    if (rdLength != 4)
                    {
                        return false;
                    }

                    records.Add(new DnsResourceRecord(rrName, rtype, rclass, ttl, Address: new IPAddress(rdata)));
                    break;
                }

                case DnsRecordType.AAAA:
                {
                    if (rdLength != 16)
                    {
                        return false;
                    }

                    records.Add(new DnsResourceRecord(rrName, rtype, rclass, ttl, Address: new IPAddress(rdata)));
                    break;
                }

                default:
                    // Skip unsupported record types (NSEC, HINFO, etc.) commonly included by Apple devices.
                    break;
            }
        }

        return true;
    }

    private static bool TryDecodeName(
        ReadOnlySpan<byte> datagram,
        ref int offset,
        int inlineLimit,
        out DnsWireName name)
    {
        name = DnsWireName.Root;
        if (offset < 0 || offset >= inlineLimit || inlineLimit > datagram.Length)
        {
            return false;
        }

        int pos = offset;
        int endOffset = -1;
        int jumps = 0;
        int decodedWireLength = 0;
        var labels = new List<string>(6);

        while (true)
        {
            int currentLimit = endOffset < 0 ? inlineLimit : datagram.Length;
            if (pos >= currentLimit)
            {
                return false;
            }

            byte len = datagram[pos];
            if (len == 0)
            {
                pos++;
                decodedWireLength++;
                if (decodedWireLength > MaxWireNameBytes)
                {
                    return false;
                }

                if (endOffset < 0)
                {
                    endOffset = pos;
                }

                break;
            }

            byte tag = (byte)(len & 0xC0);
            if (tag == 0xC0)
            {
                if (pos + 1 >= currentLimit)
                {
                    return false;
                }

                int pointer = ((len & 0x3F) << 8) | datagram[pos + 1];
                if (pointer >= datagram.Length)
                {
                    return false;
                }

                if (endOffset < 0)
                {
                    endOffset = pos + 2;
                }

                jumps++;
                if (jumps > MaxCompressionHops)
                {
                    return false;
                }

                pos = pointer;
                continue;
            }

            if (tag != 0)
            {
                return false;
            }

            pos++;
            if (pos + len > currentLimit)
            {
                return false;
            }

            decodedWireLength += 1 + len;
            if (decodedWireLength > MaxWireNameBytes)
            {
                return false;
            }

            labels.Add(Encoding.UTF8.GetString(datagram.Slice(pos, len)));
            pos += len;
        }

        offset = endOffset;
        name = labels.Count == 0 ? DnsWireName.Root : new DnsWireName(labels);
        return true;
    }

    private static void WriteEncodedName(List<byte> buffer, DnsWireName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Labels.Count == 0)
        {
            buffer.Add(0);
            return;
        }

        int totalBytes = 1; // terminating 0x00
        foreach (string label in name.Labels)
        {
            int byteCount = Encoding.UTF8.GetByteCount(label);
            if (byteCount == 0 || byteCount > MaxLabelBytes)
            {
                throw new ArgumentException($"DNS label '{label}' must be between 1 and {MaxLabelBytes} UTF-8 bytes.", nameof(name));
            }

            totalBytes += 1 + byteCount;
        }

        if (totalBytes > MaxWireNameBytes)
        {
            throw new ArgumentException($"Encoded DNS name exceeds {MaxWireNameBytes} bytes.", nameof(name));
        }

        foreach (string label in name.Labels)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(label);
            buffer.Add((byte)utf8.Length);
            buffer.AddRange(utf8);
        }

        buffer.Add(0);
    }

    private static void WriteUInt16BigEndian(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value & 0xFF));
    }
}
