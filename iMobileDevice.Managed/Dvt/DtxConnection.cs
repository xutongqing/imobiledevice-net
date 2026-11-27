namespace iMobileDevice.Managed.Dvt;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

public enum DtxAuxArgKind
{
    ArchivedObject,
    Int32,
    Int64,
}

public sealed record DtxAuxArg(DtxAuxArgKind Kind, object? Value)
{
    public static DtxAuxArg Archived(object? obj) => new(DtxAuxArgKind.ArchivedObject, obj);
    public static DtxAuxArg Int32(int value) => new(DtxAuxArgKind.Int32, value);
    public static DtxAuxArg Int64(long value) => new(DtxAuxArgKind.Int64, value);
}

public sealed record DtxMessage(
    uint ChannelCode,
    uint Identifier,
    uint ConversationIndex,
    uint Flags,
    bool ExpectsReply,
    object? Payload,
    IReadOnlyList<object?> Auxiliary);

/// <summary>
/// Pure managed Apple <c>DTXConnection</c> framing, multi-fragment reassembly, and RPC dispatcher.
/// </summary>
public sealed class DtxConnection : IAsyncDisposable
{
    private const uint MessageMagic = 0x1F3D5B79;
    private const uint MessageHeaderSize = 32;
    private const uint PayloadHeaderSize = 16;
    private const uint MaxMessageLength = 64 * 1024 * 1024; // 64 MiB cap
    private const ulong AuxMagic = 0x00000000000001F0;
    private const uint AuxKeyEmpty = 0x0000000A;

    private const uint FlagDispatch = 0x00000002;
    private const uint FlagResponse = 0x00000003;
    private const uint FlagError = 0x00000004;

    private readonly Stream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<DtxMessage>> pendingReplies = new();
    private readonly ConcurrentDictionary<(uint Channel, uint Id), (MemoryStream Buffer, uint ExpectedLength)> fragmentBuffers = new();
    private readonly ConcurrentDictionary<uint, Channel<DtxMessage>> subscribers = new();
    private readonly CancellationTokenSource readerCts = new();
    private readonly Task readerTask;
    private volatile ExceptionDispatchInfo? readFailure;
    private int nextIdentifier;
    private bool disposed;

    public DtxConnection(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        readerTask = Task.Run(() => ReadLoopAsync(readerCts.Token));
    }

    internal ChannelReader<DtxMessage> Subscribe(uint channelCode)
    {
        // A slow consumer needs the latest samples. Never block unrelated DTX replies.
        var queue = Channel.CreateBounded<DtxMessage>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        if (!subscribers.TryAdd(channelCode, queue))
        {
            throw new InvalidOperationException($"DTX channel {channelCode} already has a subscriber.");
        }
        if (readFailure is { } failure)
        {
            queue.Writer.TryComplete(failure.SourceException);
        }
        return queue.Reader;
    }

    internal void Unsubscribe(uint channelCode)
    {
        if (subscribers.TryRemove(channelCode, out var queue))
        {
            queue.Writer.TryComplete();
        }
    }

    public async Task<DtxMessage> InvokeAsync(
        uint channelCode,
        string selector,
        IReadOnlyList<DtxAuxArg>? args = null,
        bool expectsReply = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        readFailure?.Throw();
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readerCts.Token);
        var token = sendCts.Token;
        uint id = (uint)Interlocked.Increment(ref nextIdentifier);
        TaskCompletionSource<DtxMessage>? tcs = null;
        if (expectsReply)
        {
            tcs = new TaskCompletionSource<DtxMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingReplies[id] = tcs;
        }

        DtxMessage reply;
        try
        {
            byte[] auxBytes = args != null && args.Count > 0 ? SerializeAuxiliary(args) : Array.Empty<byte>();
            byte[] objBytes = NsKeyedArchiver.Archive(selector);
            byte[] packet = BuildSingleFragmentPacket(channelCode, id, conversationIndex: 0, FlagDispatch, expectsReply, auxBytes, objBytes);

            await writeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(packet, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }

            if (!expectsReply)
            {
                return new DtxMessage(channelCode, id, 0, 0, false, null, Array.Empty<object?>());
            }

            reply = await tcs!.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && readFailure != null)
        {
            // The peer can send a complete reply followed by FIN before our flush or wait resumes.
            if (tcs?.Task.IsCompletedSuccessfully != true)
                readFailure.Throw();
            reply = await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (expectsReply)
            {
                pendingReplies.TryRemove(id, out _);
            }
        }

        if ((reply.Flags & 0xFF) == FlagError)
        {
            throw new InvalidOperationException($"DTX invocation '{selector}' on channel {channelCode} returned error: {reply.Payload}");
        }
        return reply;
    }

    private static byte[] BuildSingleFragmentPacket(
        uint channelCode,
        uint identifier,
        uint conversationIndex,
        uint payloadFlags,
        bool expectsReply,
        byte[] auxBytes,
        byte[] objBytes)
    {
        ulong payloadTotal = (ulong)(auxBytes.Length + objBytes.Length);
        uint bodyLength = checked((uint)(PayloadHeaderSize + payloadTotal));
        byte[] packet = new byte[MessageHeaderSize + bodyLength];

        // 32-byte DTXMessageHeader
        Span<byte> h = packet.AsSpan(0, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(0, 4), MessageMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(4, 4), MessageHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(8, 2), 0); // fragmentId
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(10, 2), 1); // fragmentCount
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(12, 4), bodyLength);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(16, 4), identifier);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(20, 4), conversationIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(24, 4), channelCode);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(28, 4), expectsReply ? 1u : 0u);

        // 16-byte DTXMessagePayloadHeader
        Span<byte> ph = packet.AsSpan(32, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(ph.Slice(0, 4), payloadFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(ph.Slice(4, 4), (uint)auxBytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(ph.Slice(8, 8), payloadTotal);

        if (auxBytes.Length > 0)
        {
            auxBytes.CopyTo(packet.AsSpan(48));
        }

        if (objBytes.Length > 0)
        {
            objBytes.CopyTo(packet.AsSpan(48 + auxBytes.Length));
        }

        return packet;
    }

    private static byte[] SerializeAuxiliary(IReadOnlyList<DtxAuxArg> args)
    {
        using var inner = new MemoryStream();
        Span<byte> u4 = stackalloc byte[4];
        Span<byte> u8 = stackalloc byte[8];

        foreach (DtxAuxArg arg in args)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(u4, AuxKeyEmpty);
            inner.Write(u4);

            switch (arg.Kind)
            {
                case DtxAuxArgKind.ArchivedObject:
                {
                    byte[] archived = NsKeyedArchiver.Archive(arg.Value);
                    BinaryPrimitives.WriteUInt32LittleEndian(u4, 2);
                    inner.Write(u4);
                    BinaryPrimitives.WriteUInt32LittleEndian(u4, (uint)archived.Length);
                    inner.Write(u4);
                    inner.Write(archived);
                    break;
                }

                case DtxAuxArgKind.Int32:
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(u4, 3);
                    inner.Write(u4);
                    BinaryPrimitives.WriteInt32LittleEndian(u4, Convert.ToInt32(arg.Value));
                    inner.Write(u4);
                    break;
                }

                case DtxAuxArgKind.Int64:
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(u4, 4);
                    inner.Write(u4);
                    BinaryPrimitives.WriteInt64LittleEndian(u8, Convert.ToInt64(arg.Value));
                    inner.Write(u8);
                    break;
                }
            }
        }

        byte[] innerBytes = inner.ToArray();
        byte[] result = new byte[16 + innerBytes.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(0, 8), AuxMagic);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8, 8), (ulong)innerBytes.Length);
        innerBytes.CopyTo(result.AsSpan(16));
        return result;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception closeError = new EndOfStreamException("DTX connection closed.");
        byte[] header = new byte[MessageHeaderSize];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
                uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                if (magic != MessageMagic)
                {
                    throw new FormatException($"Invalid DTX message magic: 0x{magic:X8}.");
                }

                ushort fragmentId = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));
                ushort fragmentCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10, 2));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
                uint identifier = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));
                uint conversationIndex = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20, 4));
                uint channelCode = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24, 4));
                bool expectsReply = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28, 4)) != 0;

                if (length > MaxMessageLength)
                {
                    throw new FormatException($"DTX fragment length too large: {length}.");
                }

                // In Apple's DTX multi-fragment framing, fragmentId == 0 is a header-only descriptor
                // whose 'length' field states the total reassembled size across fragments 1..N-1.
                bool isDescriptorFragment = fragmentCount > 1 && fragmentId == 0;
                int bytesToRead = isDescriptorFragment ? 0 : (int)length;
                byte[] fragmentData = bytesToRead > 0 ? new byte[bytesToRead] : Array.Empty<byte>();
                if (bytesToRead > 0)
                {
                    await stream.ReadExactlyAsync(fragmentData, cancellationToken).ConfigureAwait(false);
                }

                byte[]? fullPayload = null;
                if (fragmentCount <= 1)
                {
                    fullPayload = fragmentData;
                }
                else
                {
                    var key = (channelCode, identifier);
                    var entry = fragmentBuffers.GetOrAdd(key, _ => (new MemoryStream(), isDescriptorFragment ? length : MaxMessageLength));
                    if (fragmentData.Length > 0)
                    {
                        long projected = entry.Buffer.Length + fragmentData.Length;
                        if (projected > MaxMessageLength || projected > entry.ExpectedLength)
                        {
                            fragmentBuffers.TryRemove(key, out _);
                            entry.Buffer.Dispose();
                            throw new FormatException($"DTX reassembled payload ({projected} bytes) exceeds declared/max limit ({entry.ExpectedLength} bytes).");
                        }

                        entry.Buffer.Write(fragmentData, 0, fragmentData.Length);
                    }

                    if (fragmentId == fragmentCount - 1)
                    {
                        fragmentBuffers.TryRemove(key, out _);
                        fullPayload = entry.Buffer.ToArray();
                        entry.Buffer.Dispose();
                    }
                }

                if (fullPayload == null)
                {
                    continue;
                }

                DtxMessage message = ParseCompleteMessage(channelCode, identifier, conversationIndex, expectsReply, fullPayload);
                if (conversationIndex >= 1 && pendingReplies.TryRemove(identifier, out var waiter))
                {
                    waiter.TrySetResult(message);
                }
                // Device-initiated messages carry the negated local channel number.
                if (conversationIndex == 0 && subscribers.TryGetValue(unchecked(0u - channelCode), out var subscriber))
                {
                    subscriber.Writer.TryWrite(message);
                }
                if (expectsReply && conversationIndex == 0)
                {
                    // Send empty ACK back to server if it expects a reply to a server-initiated notification
                    byte[] ack = BuildSingleFragmentPacket(channelCode, identifier, conversationIndex: 1, payloadFlags: 0, expectsReply: false, Array.Empty<byte>(), Array.Empty<byte>());
                    await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await stream.WriteAsync(ack, cancellationToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // DisposeAsync requested shutdown.
        }
        catch (Exception ex)
        {
            readFailure = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            readFailure ??= ExceptionDispatchInfo.Capture(closeError);
            Exception terminal = readFailure.SourceException;
            foreach (var subscriber in subscribers.Values)
            {
                subscriber.Writer.TryComplete(terminal);
            }
            readerCts.Cancel();
        }
    }

    private static DtxMessage ParseCompleteMessage(
        uint channelCode,
        uint identifier,
        uint conversationIndex,
        bool expectsReply,
        byte[] fullPayload)
    {
        if (fullPayload.Length < PayloadHeaderSize)
        {
            return new DtxMessage(channelCode, identifier, conversationIndex, 0, expectsReply, null, Array.Empty<object?>());
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(fullPayload.AsSpan(0, 4));
        uint auxLen = BinaryPrimitives.ReadUInt32LittleEndian(fullPayload.AsSpan(4, 4));
        ulong totalLen = BinaryPrimitives.ReadUInt64LittleEndian(fullPayload.AsSpan(8, 8));

        int auxStart = (int)PayloadHeaderSize;
        int objStart = checked(auxStart + (int)auxLen);
        int objLen = checked((int)(totalLen - auxLen));

        object? payloadObj = null;
        if (objLen > 0 && objStart + objLen <= fullPayload.Length)
        {
            ReadOnlySpan<byte> objSpan = fullPayload.AsSpan(objStart, objLen);
            payloadObj = (flags & 0xFF) == FlagResponse || (flags & 0xFF) == FlagDispatch || (flags & 0xFF) == FlagError
                ? NsKeyedArchiver.Unarchive(objSpan)
                : objSpan.ToArray();
        }

        return new DtxMessage(channelCode, identifier, conversationIndex, flags, expectsReply, payloadObj, Array.Empty<object?>());
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        readerCts.Cancel();
        await stream.DisposeAsync().ConfigureAwait(false);
        try
        {
            await readerTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore shutdown errors.
        }

        readerCts.Dispose();
    }
}
