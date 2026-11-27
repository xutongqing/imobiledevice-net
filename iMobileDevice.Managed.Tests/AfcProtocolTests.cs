namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;
using Xunit;

public class AfcProtocolTests
{
    private static byte[] BuildAfcPacket(ulong packetNum, AfcOperation op, ReadOnlySpan<byte> headerPayload, ReadOnlySpan<byte> bodyPayload)
    {
        ulong thisLen = (ulong)(40 + headerPayload.Length);
        ulong entireLen = thisLen + (ulong)bodyPayload.Length;
        byte[] buf = new byte[entireLen];
        "CFA6LPAA"u8.CopyTo(buf.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8, 8), entireLen);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(16, 8), thisLen);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(24, 8), packetNum);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(32, 8), (ulong)op);
        headerPayload.CopyTo(buf.AsSpan(40));
        bodyPayload.CopyTo(buf.AsSpan(40 + headerPayload.Length));
        return buf;
    }

    [Theory]
    [InlineData(0)] // this_length = 40 (all 3 bytes in body)
    [InlineData(1)] // this_length = 41 (1 byte in header payload, 2 in body)
    [InlineData(3)] // this_length = 43 (all 3 bytes in header payload, 0 in body)
    public async Task AfcReadFileChunkCombinesHeaderPayloadAndBodyWithoutTruncation(int headerBytes)
    {
        byte[] expected = { 0x11, 0x22, 0x33 };
        byte[] reply = BuildAfcPacket(
            packetNum: 1,
            op: AfcOperation.Data,
            headerPayload: expected.AsSpan(0, headerBytes),
            bodyPayload: expected.AsSpan(headerBytes));

        using var stream = new ScriptedDuplexStream(reply);
        await using var afc = new AfcClient(stream);

        byte[] actual = await afc.ReadFileChunkAsync(handle: 7, maxBytes: 16);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task AfcRejectsPacketSequenceMismatch()
    {
        byte[] reply = BuildAfcPacket(packetNum: 99, op: AfcOperation.Data, ReadOnlySpan<byte>.Empty, "abc"u8);
        using var stream = new ScriptedDuplexStream(reply);
        await using var afc = new AfcClient(stream);

        var ex = await Assert.ThrowsAsync<AfcException>(() => afc.ReadFileChunkAsync(handle: 1, maxBytes: 16));
        Assert.Equal(AfcErrorCode.OpHeaderInvalid, ex.ErrorCode);
    }

    [Fact]
    public async Task AfcRejectsOversizedPacketHeaderBeforeAllocating()
    {
        byte[] badHeader = new byte[40];
        "CFA6LPAA"u8.CopyTo(badHeader.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(badHeader.AsSpan(8, 8), 1024UL * 1024 * 1024); // 1 GiB
        BinaryPrimitives.WriteUInt64LittleEndian(badHeader.AsSpan(16, 8), 40);
        BinaryPrimitives.WriteUInt64LittleEndian(badHeader.AsSpan(24, 8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(badHeader.AsSpan(32, 8), (ulong)AfcOperation.Data);

        using var stream = new ScriptedDuplexStream(badHeader);
        await using var afc = new AfcClient(stream);

        var ex = await Assert.ThrowsAsync<AfcException>(() => afc.ReadFileChunkAsync(handle: 1, maxBytes: 16));
        Assert.Equal(AfcErrorCode.OpHeaderInvalid, ex.ErrorCode);
    }

    [Fact]
    public async Task CancelledAfcFileReadClosesTransportAndPreservesCancellation()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(limit.Token);
        byte[] opened = BuildAfcPacket(1, AfcOperation.FileRefOpenResult, BitConverter.GetBytes(1UL), []);
        byte[] lateReply = BuildAfcPacket(2, AfcOperation.Data, [], []);
        using var wire = new ScriptedDuplexStream(opened.Concat(lateReply).ToArray(), opened.Length);
        await using var afc = new AfcClient(wire);
        var read = afc.ReadAllBytesAsync("/test", cancel.Token);
        await wire.Paused.Task.WaitAsync(limit.Token);
        var written = wire.BytesWritten;
        cancel.Cancel();
        wire.Resume.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(limit.Token));
        Assert.True(wire.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => afc.GetDeviceInfoAsync(limit.Token));
        Assert.Equal(written, wire.BytesWritten);
    }

    [Fact]
    public async Task CancellingAQueuedAfcRequestPreservesTheActiveExchange()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancel = new CancellationTokenSource();
        var replies = BuildAfcPacket(1, AfcOperation.Data, [], []).Concat(BuildAfcPacket(2, AfcOperation.Data, [], [])).ToArray();
        using var wire = new ScriptedDuplexStream(replies, pauseAt: 0);
        await using var afc = new AfcClient(wire);
        var first = afc.GetDeviceInfoAsync(limit.Token);
        await wire.Paused.Task.WaitAsync(limit.Token);
        var queued = afc.GetDeviceInfoAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(limit.Token));
        wire.Resume.TrySetResult();
        await first;
        await afc.GetDeviceInfoAsync(limit.Token);
        Assert.False(wire.Closed);
    }

    [Fact]
    public void PairRecordVerifyRemoteDeviceCertificateAcceptsMatchAndRejectsMismatch()
    {
        using var deviceRsa = RSA.Create(2048);
        byte[] devicePubPem = Encoding.ASCII.GetBytes(deviceRsa.ExportRSAPublicKeyPem() + "\n");
        PairRecord record = PairRecord.GenerateForDevice(devicePubPem, "HOST-1", "BUID-1");

        using var matchingCert = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(record.DeviceCertificate));
        Assert.True(record.VerifyRemoteDeviceCertificate(matchingCert));

        // Different key/cert should be rejected
        using var attackerRsa = RSA.Create(2048);
        var attackerReq = new CertificateRequest("CN=Attacker", attackerRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var attackerCert = attackerReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(record.VerifyRemoteDeviceCertificate(attackerCert));
        Assert.False(record.VerifyRemoteDeviceCertificate(null));
    }

    private sealed class ScriptedDuplexStream : Stream
    {
        private readonly MemoryStream readStream;
        private readonly MemoryStream writeStream = new();

        private readonly int pauseAt;
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long BytesWritten => writeStream.Length;
        public bool Closed { get; private set; }

        public ScriptedDuplexStream(byte[] scriptedReplies, int pauseAt = -1)
        {
            this.pauseAt = pauseAt;
            readStream = new MemoryStream(scriptedReplies, writable: false);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => readStream.Length;
        public override long Position { get => readStream.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => readStream.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (readStream.Position == pauseAt && Paused.TrySetResult())
                await Resume.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await readStream.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
        public override void Write(byte[] buffer, int offset, int count) => writeStream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => writeStream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
