namespace iMobileDevice.Managed.RemoteXpc;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading.Channels;

/// <summary>
/// Pure managed cleartext HTTP/2 (h2c) transport for Apple RemoteXPC channels (Root Stream 1 &amp; Reply Stream 3),
/// with per-message-ID RPC reply routing and RFC 9113 outbound flow-control window tracking.
/// </summary>
public sealed class RemoteXpcConnection : IAsyncDisposable
{
    public const uint RootStreamId = 1;
    public const uint ReplyStreamId = 3;

    private const byte FrameData = 0x0;
    private const byte FrameHeaders = 0x1;
    private const byte FrameRstStream = 0x3;
    private const byte FrameSettings = 0x4;
    private const byte FramePing = 0x6;
    private const byte FrameGoAway = 0x7;
    private const byte FrameWindowUpdate = 0x8;

    private const byte FlagAck = 0x1;
    private const byte FlagEndHeaders = 0x4;
    private const int DefaultHttp2Window = 65535;

    private static ReadOnlySpan<byte> Http2Preface => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    private readonly Stream stream;
    private readonly Action<string>? trace;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> messageWriters = new();
    private readonly ConcurrentDictionary<uint, MemoryStream> streamBuffers = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<XpcMessage>> pendingReplies = new();
    private readonly Channel<XpcMessage> rootInbox = Channel.CreateUnbounded<XpcMessage>();
    private readonly Channel<XpcMessage> replyInbox = Channel.CreateUnbounded<XpcMessage>();
    private readonly CancellationTokenSource readerCts = new();
    private readonly Task readerTask;

    private readonly object windowLock = new();
    private readonly Dictionary<uint, long> streamSendWindows = new();
    private long connSendWindow = DefaultHttp2Window;
    private int peerInitialWindowSize = DefaultHttp2Window;
    private TaskCompletionSource windowAvailableTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ulong nextMessageId;
    private int disposed;

    private RemoteXpcConnection(Stream stream, Action<string>? trace = null)
    {
        this.stream = stream;
        this.trace = trace;
        streamSendWindows[RootStreamId] = DefaultHttp2Window;
        streamSendWindows[ReplyStreamId] = DefaultHttp2Window;
        readerTask = Task.Run(() => ReadLoopAsync(readerCts.Token));
    }

    public static async Task<RemoteXpcConnection> ConnectAsync(Stream stream, CancellationToken cancellationToken = default, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var conn = new RemoteXpcConnection(stream, trace);
        try
        {
            await conn.PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task PerformHandshakeAsync(CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 1. HTTP/2 connection preface
            await stream.WriteAsync(Http2Preface.ToArray(), cancellationToken).ConfigureAwait(false);

            // 2. Client SETTINGS: MAX_CONCURRENT_STREAMS (3) = 100, INITIAL_WINDOW_SIZE (4) = 1048576
            byte[] settings = new byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(settings.AsSpan(0, 2), 3);
            BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(2, 4), 100);
            BinaryPrimitives.WriteUInt16BigEndian(settings.AsSpan(6, 2), 4);
            BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(8, 4), 1048576);
            await WriteFrameLockedAsync(FrameSettings, 0, 0, settings, cancellationToken).ConfigureAwait(false);

            // 3. Connection WINDOW_UPDATE (+983041 -> 1 MiB window)
            byte[] winUp = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(winUp, 983041);
            await WriteFrameLockedAsync(FrameWindowUpdate, 0, 0, winUp, cancellationToken).ConfigureAwait(false);

            // 4. Open Root channel (Stream 1): empty dict (0x00000001) + init stream marker (0x00000201)
            await WriteFrameLockedAsync(FrameHeaders, FlagEndHeaders, RootStreamId, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
            byte[] rootInit = XpcSerializer.SerializeWrapper(XpcWrapperFlags.AlwaysSet, 0, new Dictionary<string, object?>());
            await WriteFrameLockedAsync(FrameData, 0, RootStreamId, rootInit, cancellationToken).ConfigureAwait(false);
            byte[] rootInitMarker = XpcSerializer.SerializeWrapper(XpcWrapperFlags.AlwaysSet | XpcWrapperFlags.InitStreamData, 0, null);
            await WriteFrameLockedAsync(FrameData, 0, RootStreamId, rootInitMarker, cancellationToken).ConfigureAwait(false);

            // 5. Open Reply channel (Stream 3): init handshake (0x00400001) with empty body
            await WriteFrameLockedAsync(FrameHeaders, FlagEndHeaders, ReplyStreamId, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
            byte[] replyInit = XpcSerializer.SerializeWrapper(XpcWrapperFlags.AlwaysSet | XpcWrapperFlags.InitHandshake, 0, null);
            await WriteFrameLockedAsync(FrameData, 0, ReplyStreamId, replyInit, cancellationToken).ConfigureAwait(false);

            ConsumeSendWindow(RootStreamId, rootInit.Length + rootInitMarker.Length);
            ConsumeSendWindow(ReplyStreamId, replyInit.Length);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<ulong> SendAsync(IDictionary<string, object?> body, bool expectingReply = true, uint streamId = RootStreamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ulong msgId = Interlocked.Increment(ref nextMessageId);
        XpcWrapperFlags flags = XpcWrapperFlags.AlwaysSet | XpcWrapperFlags.DataFlag;
        if (expectingReply)
        {
            flags |= XpcWrapperFlags.WantingReply;
        }

        byte[] payload = XpcSerializer.SerializeWrapper(flags, msgId, body);
        await SendDataWithFlowControlAsync(streamId, payload, cancellationToken).ConfigureAwait(false);
        return msgId;
    }

    public ValueTask<XpcMessage> ReceiveRootMessageAsync(CancellationToken cancellationToken = default) =>
        rootInbox.Reader.ReadAsync(cancellationToken);

    public ValueTask<XpcMessage> ReceiveReplyMessageAsync(CancellationToken cancellationToken = default) =>
        replyInbox.Reader.ReadAsync(cancellationToken);

    public async Task<XpcMessage> SendAndReceiveAsync(IDictionary<string, object?> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ulong msgId = Interlocked.Increment(ref nextMessageId);
        var tcs = new TaskCompletionSource<XpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingReplies[msgId] = tcs;

        try
        {
            XpcWrapperFlags flags = XpcWrapperFlags.AlwaysSet | XpcWrapperFlags.DataFlag | XpcWrapperFlags.WantingReply;
            byte[] payload = XpcSerializer.SerializeWrapper(flags, msgId, request);
            await SendDataWithFlowControlAsync(RootStreamId, payload, cancellationToken).ConfigureAwait(false);

            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            pendingReplies.TryRemove(msgId, out _);
        }
    }

    private async Task SendDataWithFlowControlAsync(uint streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        const int maxFramePayload = 16384;
        var messageWriter = messageWriters.GetOrAdd(streamId, static _ => new SemaphoreSlim(1, 1));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readerCts.Token);
        var token = lifetime.Token;
        await messageWriter.WaitAsync(token).ConfigureAwait(false);
        bool started = false;
        try
        {
            for (int offset = 0; offset < payload.Length;)
            {
                int count = await WaitForSendWindowAsync(streamId, Math.Min(maxFramePayload, payload.Length - offset), token).ConfigureAwait(false);
                try
                {
                    await writeLock.WaitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    // No frame took ownership of this reservation.
                    CreditSendWindow(0, (uint)count);
                    CreditSendWindow(streamId, (uint)count);
                    throw;
                }

                try
                {
                    started = true;
                    await WriteFrameLockedAsync(FrameData, 0, streamId, payload.Slice(offset, count), token).ConfigureAwait(false);
                    offset += count;
                    if (offset == payload.Length)
                    {
                        await stream.FlushAsync(token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    writeLock.Release();
                }
            }
        }
        catch when (started)
        {
            // A following wrapper cannot start in the middle of an abandoned one.
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            messageWriter.Release();
        }
    }

    private async Task<int> WaitForSendWindowAsync(uint streamId, int desiredBytes, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (windowLock)
            {
                if (!streamSendWindows.TryGetValue(streamId, out long streamWin))
                {
                    streamWin = peerInitialWindowSize;
                    streamSendWindows[streamId] = streamWin;
                }

                long avail = Math.Min(connSendWindow, streamWin);
                if (avail > 0)
                {
                    int grant = (int)Math.Min(desiredBytes, avail);
                    connSendWindow -= grant;
                    streamSendWindows[streamId] = streamWin - grant;
                    return grant;
                }

                waitTask = windowAvailableTcs.Task;
            }

            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ConsumeSendWindow(uint streamId, int bytes)
    {
        lock (windowLock)
        {
            connSendWindow -= bytes;
            long current = streamSendWindows.TryGetValue(streamId, out long w) ? w : peerInitialWindowSize;
            streamSendWindows[streamId] = current - bytes;
        }
    }

    private void CreditSendWindow(uint streamId, uint increment)
    {
        lock (windowLock)
        {
            if (streamId == 0)
            {
                connSendWindow += increment;
            }
            else
            {
                long current = streamSendWindows.TryGetValue(streamId, out long w) ? w : peerInitialWindowSize;
                streamSendWindows[streamId] = current + increment;
            }

            var prev = windowAvailableTcs;
            windowAvailableTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            prev.TrySetResult();
        }
    }

    private void ApplyPeerSettings(ReadOnlySpan<byte> payload)
    {
        for (int i = 0; i + 6 <= payload.Length; i += 6)
        {
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(i, 2));
            uint val = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(i + 2, 4));
            if (id == 4 && val <= int.MaxValue) // SETTINGS_INITIAL_WINDOW_SIZE
            {
                lock (windowLock)
                {
                    long delta = (long)val - peerInitialWindowSize;
                    peerInitialWindowSize = (int)val;
                    foreach (uint sid in streamSendWindows.Keys.ToList())
                    {
                        streamSendWindows[sid] += delta;
                    }

                    var prev = windowAvailableTcs;
                    windowAvailableTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    prev.TrySetResult();
                }
            }
        }
    }

    private async Task WriteFrameLockedAsync(byte type, byte flags, uint streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        byte[] header = new byte[9];
        int len = payload.Length;
        header[0] = (byte)((len >> 16) & 0xFF);
        header[1] = (byte)((len >> 8) & 0xFF);
        header[2] = (byte)(len & 0xFF);
        header[3] = type;
        header[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5, 4), streamId & 0x7FFFFFFFu);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[9];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
                int len = (header[0] << 16) | (header[1] << 8) | header[2];
                byte type = header[3];
                byte flags = header[4];
                uint streamId = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4)) & 0x7FFFFFFFu;

                byte[] payload = len > 0 ? new byte[len] : Array.Empty<byte>();
                if (len > 0)
                {
                    await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
                }

                trace?.Invoke($"H2 RX type={type} flags=0x{flags:X2} stream={streamId} len={len}");

                switch (type)
                {
                    case FrameSettings:
                        if ((flags & FlagAck) == 0)
                        {
                            ApplyPeerSettings(payload);
                            await SendControlFrameAsync(FrameSettings, FlagAck, 0, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
                        }
                        break;

                    case FramePing:
                        if ((flags & FlagAck) == 0)
                        {
                            await SendControlFrameAsync(FramePing, FlagAck, 0, payload, cancellationToken).ConfigureAwait(false);
                        }
                        break;

                    case FrameWindowUpdate:
                        if (payload.Length >= 4)
                        {
                            uint inc = BinaryPrimitives.ReadUInt32BigEndian(payload) & 0x7FFFFFFFu;
                            if (inc > 0)
                            {
                                CreditSendWindow(streamId, inc);
                            }
                        }
                        break;

                    case FrameData:
                        if (len > 0)
                        {
                            byte[] winUp = new byte[4];
                            BinaryPrimitives.WriteUInt32BigEndian(winUp, (uint)len);
                            await SendControlFrameAsync(FrameWindowUpdate, 0, 0, winUp, cancellationToken).ConfigureAwait(false);
                            if (streamId != 0)
                            {
                                await SendControlFrameAsync(FrameWindowUpdate, 0, streamId, winUp, cancellationToken).ConfigureAwait(false);
                            }

                            OnDataReceived(streamId, payload);
                        }
                        break;

                    case FrameGoAway:
                        FailPendingWaiters(new EndOfStreamException("RemoteXPC peer sent HTTP/2 GOAWAY."));
                        return;

                    case FrameRstStream:
                        if (payload.Length >= 4)
                        {
                            trace?.Invoke($"H2 RST_STREAM stream={streamId} errorCode=0x{BinaryPrimitives.ReadUInt32BigEndian(payload):X8}");
                        }
                        break;

                    case FrameHeaders:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            FailPendingWaiters(ex);
        }
        finally
        {
            FailPendingWaiters(new EndOfStreamException("RemoteXPC connection closed."));
            readerCts.Cancel();
        }
    }

    private void FailPendingWaiters(Exception ex)
    {
        rootInbox.Writer.TryComplete(ex);
        replyInbox.Writer.TryComplete(ex);
        foreach (var kv in pendingReplies)
        {
            kv.Value.TrySetException(ex);
        }

        lock (windowLock)
        {
            windowAvailableTcs.TrySetException(ex);
        }
    }

    private async Task SendControlFrameAsync(byte type, byte flags, uint streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrameLockedAsync(type, flags, streamId, payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void OnDataReceived(uint streamId, byte[] chunk)
    {
        MemoryStream buf = streamBuffers.GetOrAdd(streamId, _ => new MemoryStream());
        buf.Position = buf.Length;
        buf.Write(chunk, 0, chunk.Length);

        byte[] raw = buf.ToArray();
        int offset = 0;
        while (offset < raw.Length && XpcSerializer.TryParseWrapper(raw.AsSpan(offset), out XpcMessage msg, out int consumed))
        {
            offset += consumed;
            trace?.Invoke($"XPC RX stream={streamId} flags=0x{(uint)msg.Flags:X8} msgId={msg.MessageId} bodyType={msg.Body?.GetType().Name ?? "null"}");
            if (msg.Body == null)
            {
                continue;
            }

            if (msg.MessageId != 0 && pendingReplies.TryRemove(msg.MessageId, out var waiter))
            {
                waiter.TrySetResult(msg);
            }
            else if (streamId == ReplyStreamId || (msg.Flags & XpcWrapperFlags.Reply) != 0)
            {
                replyInbox.Writer.TryWrite(msg);
            }
            else
            {
                rootInbox.Writer.TryWrite(msg);
            }
        }

        if (offset > 0)
        {
            int remaining = raw.Length - offset;
            buf.SetLength(0);
            if (remaining > 0)
            {
                buf.Write(raw, offset, remaining);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

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
