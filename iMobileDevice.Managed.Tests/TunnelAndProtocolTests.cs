namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Net;
using iMobileDevice.Managed.Dvt;
using iMobileDevice.Managed.RemoteXpc;
using iMobileDevice.Managed.Tunnel;
using Xunit;

public class TunnelAndProtocolTests
{
    [Fact]
    public void XpcSerializerRoundTripsAllSupportedTypes()
    {
        var guid = Guid.NewGuid();
        var dict = new Dictionary<string, object?>
        {
            ["str"] = "com.apple.instruments.dtservicehub",
            ["unicode"] = "测试XPC",
            ["boolT"] = true,
            ["boolF"] = false,
            ["i64"] = -123456789L,
            ["u64"] = 9876543210UL,
            ["dbl"] = 2.718281828,
            ["data"] = new byte[] { 1, 2, 3, 4, 5 },
            ["uuid"] = guid,
            ["arr"] = new List<object?> { "x", 42L, true },
            ["nested"] = new Dictionary<string, object?> { ["Port"] = "49805" },
        };

        byte[] wrapped = XpcSerializer.SerializeWrapper(XpcWrapperFlags.AlwaysSet | XpcWrapperFlags.DataFlag, 7, dict);
        Assert.True(XpcSerializer.TryParseWrapper(wrapped, out XpcMessage msg, out int consumed));
        Assert.Equal(wrapped.Length, consumed);
        Assert.Equal(7UL, msg.MessageId);
        Assert.True((msg.Flags & XpcWrapperFlags.AlwaysSet) != 0);

        var parsed = Assert.IsType<Dictionary<string, object?>>(msg.Body);
        Assert.Equal("com.apple.instruments.dtservicehub", parsed["str"]);
        Assert.Equal("测试XPC", parsed["unicode"]);
        Assert.Equal(true, parsed["boolT"]);
        Assert.Equal(false, parsed["boolF"]);
        Assert.Equal(-123456789L, parsed["i64"]);
        Assert.Equal(9876543210UL, parsed["u64"]);
        Assert.Equal(2.718281828, (double)parsed["dbl"]!, precision: 9);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, parsed["data"]);
        Assert.Equal(guid, parsed["uuid"]);
        Assert.Equal(3, Assert.IsType<List<object?>>(parsed["arr"]).Count);
        Assert.Equal("49805", Assert.IsType<Dictionary<string, object?>>(parsed["nested"])["Port"]);
    }

    [Fact]
    public void NsKeyedArchiverRoundTripsStringsDictsAndData()
    {
        var capabilities = new Dictionary<string, object?>
        {
            ["com.apple.private.DTXBlockCompression"] = 2L,
            ["com.apple.private.DTXConnection"] = 1L,
            ["label"] = "takeScreenshot",
            ["bytes"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        };

        byte[] archived = NsKeyedArchiver.Archive(capabilities);
        var restored = Assert.IsType<Dictionary<string, object?>>(NsKeyedArchiver.Unarchive(archived));
        Assert.Equal(2L, restored["com.apple.private.DTXBlockCompression"]);
        Assert.Equal(1L, restored["com.apple.private.DTXConnection"]);
        Assert.Equal("takeScreenshot", restored["label"]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, restored["bytes"]);
    }

    [Fact]
    public async Task DtxConnectionReassemblesMultiFragmentResponseWithDescriptorFragment0()
    {
        byte[] fakePng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04 };
        byte[] archivedObj = NsKeyedArchiver.Archive(fakePng);

        // Build 16-byte payload header + archived object
        byte[] fullPayload = new byte[16 + archivedObj.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(fullPayload.AsSpan(0, 4), 3); // FlagResponse
        BinaryPrimitives.WriteUInt32LittleEndian(fullPayload.AsSpan(4, 4), 0); // auxLen
        BinaryPrimitives.WriteUInt64LittleEndian(fullPayload.AsSpan(8, 8), (ulong)archivedObj.Length);
        archivedObj.CopyTo(fullPayload.AsSpan(16));

        int split = fullPayload.Length / 2;
        using var wire = new MemoryStream();
        // Fragment 0: header-only descriptor with total length
        WriteDtxHeader(wire, fragId: 0, fragCount: 3, length: (uint)fullPayload.Length, id: 1, convIdx: 1, chan: 1);
        // Fragment 1: first half
        WriteDtxHeader(wire, fragId: 1, fragCount: 3, length: (uint)split, id: 1, convIdx: 1, chan: 1);
        wire.Write(fullPayload, 0, split);
        // Fragment 2: second half
        WriteDtxHeader(wire, fragId: 2, fragCount: 3, length: (uint)(fullPayload.Length - split), id: 1, convIdx: 1, chan: 1);
        wire.Write(fullPayload, split, fullPayload.Length - split);

        using var duplex = new PairedMemoryStream(wire.ToArray());
        await using var dtx = new DtxConnection(duplex);

        DtxMessage reply = await dtx.InvokeAsync(channelCode: 1, selector: "takeScreenshot", expectsReply: true);
        Assert.Equal(fakePng, Assert.IsType<byte[]>(reply.Payload));
    }

    [Fact]
    public async Task SlowDtxSubscriberReceivesLatestEventsWithoutBlockingRepliesOrEof()
    {
        using var wire = new MemoryStream();
        // The device uses a negative wire channel for its own messages. IDs can match a pending RPC.
        for (uint id = 1; id <= 6; id++)
        {
            WriteDtxHeader(wire, fragId: 0, fragCount: 1, length: 16, id: id, convIdx: 0, chan: unchecked((uint)-2));
            wire.Write(new byte[16]);
        }
        WriteDtxHeader(wire, fragId: 0, fragCount: 1, length: 16, id: 1, convIdx: 1, chan: 1);
        wire.Write(new byte[16]);

        using var duplex = new PairedMemoryStream(wire.ToArray());
        await using var dtx = new DtxConnection(duplex);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = dtx.Subscribe(2);
        var otherChannel = dtx.Subscribe(3);
        var reply = await dtx.InvokeAsync(1, "runningProcesses", cancellationToken: timeout.Token);
        Assert.Equal(1u, reply.ConversationIndex);
        for (uint id = 3; id <= 6; id++)
        {
            var message = await events.ReadAsync(timeout.Token);
            Assert.Equal(id, message.Identifier);
            Assert.Equal(0u, message.ConversationIndex);
        }
        await Assert.ThrowsAsync<EndOfStreamException>(() => events.Completion.WaitAsync(timeout.Token));
        await Assert.ThrowsAsync<EndOfStreamException>(() => otherChannel.Completion.WaitAsync(timeout.Token));
        await Assert.ThrowsAsync<IOException>(() => dtx.InvokeAsync(1, "runningProcesses", cancellationToken: timeout.Token));
    }

    [Fact]
    public void Ipv6TcpChecksumValidatesZeroWhenIncluded()
    {
        byte[] src = IPAddress.Parse("fd48:4e1c:27ff::2").GetAddressBytes();
        byte[] dst = IPAddress.Parse("fd48:4e1c:27ff::1").GetAddressBytes();
        byte[] tcp = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(0, 2), 50001);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(2, 2), 53544);
        tcp[12] = 0x60;
        tcp[13] = 0x02;
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(14, 2), 65535);

        ushort csum = UserspaceTcpTunnel.ComputeIpv6TcpChecksum(src, dst, tcp);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(16, 2), csum);
        Assert.Equal(0, UserspaceTcpTunnel.ComputeIpv6TcpChecksum(src, dst, tcp));
    }

    private static void WriteDtxHeader(Stream s, ushort fragId, ushort fragCount, uint length, uint id, uint convIdx, uint chan)
    {
        byte[] h = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0, 4), 0x1F3D5B79);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4, 4), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(8, 2), fragId);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(10, 2), fragCount);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(12, 4), length);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(20, 4), convIdx);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(24, 4), chan);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(28, 4), 0);
        s.Write(h);
    }

    private sealed class PairedMemoryStream : Stream
    {
        private readonly MemoryStream reader;
        private readonly MemoryStream writer = new();
        private readonly TaskCompletionSource firstWriteTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PairedMemoryStream(byte[] scriptedReply)
        {
            reader = new MemoryStream(scriptedReply, writable: false);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => reader.Length;
        public override long Position { get => reader.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await firstWriteTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            writer.Write(buffer, offset, count);
            firstWriteTcs.TrySetResult();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            writer.Write(buffer.Span);
            firstWriteTcs.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
