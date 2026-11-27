namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Net;
using System.Text;
using iMobileDevice.Managed.Bonjour;
using Xunit;

public class BonjourWireCodecTests
{
    [Theory]
    [InlineData(false, 0x0001)]
    [InlineData(true, 0x8001)]
    public void EncodesPtrQueryWithExpectedHeaderAndQClass(bool unicastResponse, ushort expectedQClass)
    {
        byte[] packet = DnsWireCodec.EncodePtrQuery("_remotepairing._tcp.local", unicastResponse);

        Assert.True(DnsWireCodec.TryDecodeMessage(packet, out DnsMessage decoded));
        Assert.False(decoded.IsResponse);
        Assert.Equal(0, decoded.TransactionId);
        Assert.Equal(0, decoded.Flags);
        DnsQuestion question = Assert.Single(decoded.Questions);
        Assert.Equal(BonjourServiceTypes.RemotePairing, question.Name.DisplayName);
        Assert.Equal(DnsRecordType.PTR, question.Type);
        Assert.Equal(unicastResponse, question.UnicastResponse);

        ushort wireQClass = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(packet.Length - 2, 2));
        Assert.Equal(expectedQClass, wireQClass);
    }

    [Fact]
    public void RejectsOversizedLabelAndInvalidNames()
    {
        string tooLongLabel = new string('a', 64) + "._tcp.local.";
        Assert.Throws<ArgumentException>(() => DnsWireCodec.EncodeName(tooLongLabel));

        string tooLongName = string.Join('.', Enumerable.Repeat(new string('b', 60), 5)) + ".local.";
        Assert.Throws<ArgumentException>(() => DnsWireCodec.EncodeName(tooLongName));

        Assert.Throws<ArgumentException>(() => DnsWireCodec.EncodeName("foo..local."));
        Assert.Throws<ArgumentException>(() => DnsWireCodec.EncodeName(".."));
    }

    [Fact]
    public void DecodesCompressionPointersAcrossRrNamePtrAndSrvTarget()
    {
        // Build a valid DNS response using 0xC0xx compression pointers:
        // Header (12 bytes): TXID=0, Flags=0x8400, QD=0, AN=2, NS=0, AR=0
        var buf = new List<byte>();
        WriteUInt16(buf, 0x0000);
        WriteUInt16(buf, 0x8400);
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 2);
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 0);

        // Offset 12: RR #1 NAME = _remotepairing._tcp.local.
        int serviceNameOffset = buf.Count; // 12
        buf.AddRange(DnsWireCodec.EncodeName("_remotepairing._tcp.local."));
        WriteUInt16(buf, (ushort)DnsRecordType.PTR);
        WriteUInt16(buf, 1); // CLASS_IN
        WriteUInt32(buf, 120);
        // PTR RDATA: "dev1." + pointer to serviceNameOffset (12)
        int ptrRdLenPos = buf.Count;
        WriteUInt16(buf, 0);
        int ptrRdataStart = buf.Count;
        buf.Add(4);
        buf.AddRange(Encoding.ASCII.GetBytes("dev1"));
        WritePointer(buf, serviceNameOffset);
        BinaryPrimitives.WriteUInt16BigEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buf).Slice(ptrRdLenPos, 2),
            (ushort)(buf.Count - ptrRdataStart));

        // RR #2 NAME = pointer to ptrRdataStart ("dev1._remotepairing._tcp.local.")
        WritePointer(buf, ptrRdataStart);
        WriteUInt16(buf, (ushort)DnsRecordType.SRV);
        WriteUInt16(buf, 0x8001); // CLASS_IN | cache-flush bit
        WriteUInt32(buf, 120);
        int srvRdLenPos = buf.Count;
        WriteUInt16(buf, 0);
        int srvRdataStart = buf.Count;
        WriteUInt16(buf, 0); // priority
        WriteUInt16(buf, 0); // weight
        WriteUInt16(buf, 49152); // port
        // Target = "iphone." + pointer to ".local." inside serviceNameOffset
        int localLabelOffset = serviceNameOffset + 1 + "_remotepairing".Length + 1 + "_tcp".Length;
        buf.Add(6);
        buf.AddRange(Encoding.ASCII.GetBytes("iphone"));
        WritePointer(buf, localLabelOffset);
        BinaryPrimitives.WriteUInt16BigEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buf).Slice(srvRdLenPos, 2),
            (ushort)(buf.Count - srvRdataStart));

        byte[] datagram = buf.ToArray();
        Assert.True(DnsWireCodec.TryDecodeMessage(datagram, out DnsMessage message));
        Assert.True(message.IsResponse);
        Assert.Equal(2, message.Answers.Count);

        DnsResourceRecord ptr = message.Answers[0];
        Assert.Equal("_remotepairing._tcp.local.", ptr.Name.DisplayName);
        Assert.Equal(DnsRecordType.PTR, ptr.Type);
        Assert.Equal("dev1._remotepairing._tcp.local.", ptr.TargetName?.DisplayName);

        DnsResourceRecord srv = message.Answers[1];
        Assert.Equal("dev1._remotepairing._tcp.local.", srv.Name.DisplayName);
        Assert.Equal(DnsRecordType.SRV, srv.Type);
        Assert.Equal(DnsWireCodec.ClassIn, srv.Class); // 0x8000 cache-flush bit stripped
        Assert.Equal((ushort)49152, srv.Port);
        Assert.Equal("iphone.local.", srv.TargetName?.DisplayName);
    }

    [Fact]
    public void RejectsCompressionPointerCyclesAndOutOfBoundsPointers()
    {
        // Self-referencing pointer cycle at offset 12: 0xC0 0x0C
        byte[] selfLoop =
        [
            0x00, 0x00, 0x84, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x0C,
            0x00, 0x0C, 0x00, 0x01,
        ];
        Assert.False(DnsWireCodec.TryDecodeMessage(selfLoop, out _));

        // Mutual 2-node cycle: offset 12 -> 14, offset 14 -> 12
        byte[] mutualLoop =
        [
            0x00, 0x00, 0x84, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x0E,
            0xC0, 0x0C,
            0x00, 0x0C, 0x00, 0x01,
        ];
        Assert.False(DnsWireCodec.TryDecodeMessage(mutualLoop, out _));

        // Out-of-bounds pointer target
        byte[] outOfBounds =
        [
            0x00, 0x00, 0x84, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0xFF,
            0x00, 0x0C, 0x00, 0x01,
        ];
        Assert.False(DnsWireCodec.TryDecodeMessage(outOfBounds, out _));
    }

    [Fact]
    public void RejectsTruncatedPacketsAndMalformedRecords()
    {
        // Shorter than 12-byte header
        Assert.False(DnsWireCodec.TryDecodeMessage([0x00, 0x00, 0x84, 0x00], out _));

        // Header claims 1 Answer, but nothing follows
        byte[] missingAnswer = new byte[12];
        missingAnswer[2] = 0x84;
        missingAnswer[7] = 1;
        Assert.False(DnsWireCodec.TryDecodeMessage(missingAnswer, out _));

        // Answer A record with invalid RDLENGTH = 3 (must be 4)
        var badA = new List<byte>();
        WriteUInt16(badA, 0);
        WriteUInt16(badA, 0x8400);
        WriteUInt16(badA, 0);
        WriteUInt16(badA, 1);
        WriteUInt16(badA, 0);
        WriteUInt16(badA, 0);
        badA.AddRange(DnsWireCodec.EncodeName("host.local."));
        WriteUInt16(badA, (ushort)DnsRecordType.A);
        WriteUInt16(badA, 1);
        WriteUInt32(badA, 120);
        WriteUInt16(badA, 3);
        badA.AddRange([192, 168, 1]);
        Assert.False(DnsWireCodec.TryDecodeMessage(badA.ToArray(), out _));
    }

    [Fact]
    public void ParsesTxtKeyValuesBooleansAndEmptyZeroByte()
    {
        Assert.True(DnsWireCodec.TryParseTxtRecords([0x00], out var emptyTxt));
        Assert.Empty(emptyTxt);

        byte[] rdata =
        [
            .. PrefixLen("name=My iPhone"),
            .. PrefixLen("authTag=abc=123=="),
            .. PrefixLen("paired"),
            .. PrefixLen("empty="),
            .. PrefixLen("=ignored"),
            .. PrefixLen("name=IgnoredDuplicate"),
        ];

        Assert.True(DnsWireCodec.TryParseTxtRecords(rdata, out var parsed));
        Assert.Equal(4, parsed.Count);
        Assert.Equal("My iPhone", parsed["NAME"]); // case-insensitive
        Assert.Equal("abc=123==", parsed["authTag"]);
        Assert.True(parsed.ContainsKey("paired"));
        Assert.Null(parsed["paired"]);
        Assert.Equal(string.Empty, parsed["empty"]);
        Assert.False(parsed.ContainsKey(string.Empty));
    }

    [Fact]
    public void ParsesAnswersAuthoritiesAdditionalAndSkipsUnknownAndNonInRecords()
    {
        var buf = new List<byte>();
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 0x8400);
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 1); // AN
        WriteUInt16(buf, 1); // NS
        WriteUInt16(buf, 2); // AR (one NSEC + one A)

        // AN: A record
        buf.AddRange(DnsWireCodec.EncodeName("an.local."));
        WriteUInt16(buf, (ushort)DnsRecordType.A);
        WriteUInt16(buf, 0x8001);
        WriteUInt32(buf, 120);
        WriteUInt16(buf, 4);
        buf.AddRange(IPAddress.Parse("10.0.0.1").GetAddressBytes());

        // NS: TXT record
        buf.AddRange(DnsWireCodec.EncodeName("ns.local."));
        WriteUInt16(buf, (ushort)DnsRecordType.TXT);
        WriteUInt16(buf, 0x8001);
        WriteUInt32(buf, 120);
        byte[] txt = PrefixLen("k=v");
        WriteUInt16(buf, (ushort)txt.Length);
        buf.AddRange(txt);

        // AR #1: NSEC (type 47) should be skipped without failing
        buf.AddRange(DnsWireCodec.EncodeName("ar.local."));
        WriteUInt16(buf, 47);
        WriteUInt16(buf, 0x8001);
        WriteUInt32(buf, 120);
        WriteUInt16(buf, 2);
        buf.AddRange([0x00, 0x01]);

        // AR #2: EDNS0 OPT (type 41, class=1440 != 1) should be skipped without failing
        buf.Add(0); // root
        WriteUInt16(buf, 41);
        WriteUInt16(buf, 1440);
        WriteUInt32(buf, 0);
        WriteUInt16(buf, 0);

        Assert.True(DnsWireCodec.TryDecodeMessage(buf.ToArray(), out DnsMessage msg));
        Assert.Single(msg.Answers);
        Assert.Single(msg.Authorities);
        Assert.Empty(msg.Additional);
        Assert.Equal("10.0.0.1", msg.Answers[0].Address?.ToString());
        Assert.Equal("v", msg.Authorities[0].TxtRecords?["k"]);
    }

    [Fact]
    public void DecodesResponseContainingQuestionAndAnswerSections()
    {
        var buf = new List<byte>();
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 0x8400);
        WriteUInt16(buf, 1); // QDCOUNT = 1
        WriteUInt16(buf, 1); // ANCOUNT = 1
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 0);

        buf.AddRange(DnsWireCodec.EncodeName("_remotepairing._tcp.local."));
        WriteUInt16(buf, (ushort)DnsRecordType.PTR);
        WriteUInt16(buf, 0x0001);

        buf.AddRange(DnsWireCodec.EncodeName("_remotepairing._tcp.local."));
        WriteUInt16(buf, (ushort)DnsRecordType.PTR);
        WriteUInt16(buf, 0x0001);
        WriteUInt32(buf, 120);
        byte[] target = DnsWireCodec.EncodeName("dev._remotepairing._tcp.local.");
        WriteUInt16(buf, (ushort)target.Length);
        buf.AddRange(target);

        Assert.True(DnsWireCodec.TryDecodeMessage(buf.ToArray(), out DnsMessage msg));
        Assert.True(msg.IsResponse);
        Assert.Single(msg.Questions);
        DnsResourceRecord ans = Assert.Single(msg.Answers);
        Assert.Equal("dev._remotepairing._tcp.local.", ans.TargetName?.DisplayName);
    }

    [Theory]
    [InlineData("Dr..Phone")]
    [InlineData("iPhone v1.2")]
    [InlineData("TrailingDot.")]
    public void PreservesSingleLabelDotsAndDoubleDotsAcrossDecodeAndReEncode(string instanceLabel)
    {
        var expectedName = new DnsWireName([instanceLabel, "_remotepairing", "_tcp", "local"]);
        byte[] wireTarget = DnsWireCodec.EncodeName(expectedName);

        var buf = new List<byte>();
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 0x8400);
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 1);
        WriteUInt16(buf, 0);
        WriteUInt16(buf, 0);
        buf.AddRange(DnsWireCodec.EncodeName(BonjourServiceTypes.RemotePairing));
        WriteUInt16(buf, (ushort)DnsRecordType.PTR);
        WriteUInt16(buf, 0x0001);
        WriteUInt32(buf, 120);
        WriteUInt16(buf, (ushort)wireTarget.Length);
        buf.AddRange(wireTarget);

        Assert.True(DnsWireCodec.TryDecodeMessage(buf.ToArray(), out DnsMessage msg));
        DnsResourceRecord ptr = Assert.Single(msg.Answers);
        Assert.NotNull(ptr.TargetName);
        Assert.Equal([instanceLabel, "_remotepairing", "_tcp", "local"], ptr.TargetName!.Labels);
        Assert.Equal($"{instanceLabel}._remotepairing._tcp.local.", ptr.TargetName.DisplayName);

        byte[] reEncoded = DnsWireCodec.EncodeQuestions([new DnsQuestion(ptr.TargetName, DnsRecordType.SRV)]);
        Assert.True(DnsWireCodec.TryDecodeMessage(reEncoded, out DnsMessage questionMsg));
        DnsQuestion q = Assert.Single(questionMsg.Questions);
        Assert.Equal([instanceLabel, "_remotepairing", "_tcp", "local"], q.Name.Labels);

        // First wire label length byte at offset 12 must equal the UTF-8 length of instanceLabel (not split on '.')
        Assert.Equal((byte)Encoding.UTF8.GetByteCount(instanceLabel), reEncoded[12]);
    }

    [Fact]
    public void DnsWireNameEqualityIsCaseInsensitiveAndPreservesLabelBoundaries()
    {
        var singleLabelWithDot = new DnsWireName(["a.b", "_remotepairing", "_tcp", "local"]);
        var splitLabels = new DnsWireName(["a", "b", "_remotepairing", "_tcp", "local"]);
        var mixedCase = new DnsWireName(["A.B", "_REMOTEPAIRING", "_tcp", "LOCAL"]);

        Assert.NotEqual(singleLabelWithDot, splitLabels);
        Assert.Equal(singleLabelWithDot, mixedCase);
        Assert.Equal(singleLabelWithDot.GetHashCode(), mixedCase.GetHashCode());

        // A 63-byte 0xFF invalid-UTF-8 label decodes to 63 U+FFFD chars (189 UTF-8 bytes) and is flagged unencodable.
        var badBuf = new List<byte>();
        WriteUInt16(badBuf, 0);
        WriteUInt16(badBuf, 0x8400);
        WriteUInt16(badBuf, 0);
        WriteUInt16(badBuf, 1);
        WriteUInt16(badBuf, 0);
        WriteUInt16(badBuf, 0);
        badBuf.AddRange(DnsWireCodec.EncodeName(BonjourServiceTypes.RemotePairing));
        WriteUInt16(badBuf, (ushort)DnsRecordType.PTR);
        WriteUInt16(badBuf, 0x0001);
        WriteUInt32(badBuf, 120);
        WriteUInt16(badBuf, (ushort)(1 + 63 + 1));
        badBuf.Add(63);
        badBuf.AddRange(Enumerable.Repeat((byte)0xFF, 63));
        badBuf.Add(0);

        Assert.True(DnsWireCodec.TryDecodeMessage(badBuf.ToArray(), out DnsMessage badMsg));
        DnsWireName? decodedInvalidUtf8 = badMsg.Answers[0].TargetName;
        Assert.NotNull(decodedInvalidUtf8);
        Assert.False(DnsWireCodec.CanEncodeName(decodedInvalidUtf8));
    }

    private static byte[] PrefixLen(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        return [(byte)utf8.Length, .. utf8];
    }

    private static void WriteUInt16(List<byte> buf, ushort val)
    {
        buf.Add((byte)(val >> 8));
        buf.Add((byte)(val & 0xFF));
    }

    private static void WriteUInt32(List<byte> buf, uint val)
    {
        buf.Add((byte)(val >> 24));
        buf.Add((byte)((val >> 16) & 0xFF));
        buf.Add((byte)((val >> 8) & 0xFF));
        buf.Add((byte)(val & 0xFF));
    }

    private static void WritePointer(List<byte> buf, int targetOffset)
    {
        ushort ptr = (ushort)(0xC000 | targetOffset);
        WriteUInt16(buf, ptr);
    }
}
