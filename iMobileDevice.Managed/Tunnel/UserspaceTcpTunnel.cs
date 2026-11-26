namespace iMobileDevice.Managed.Tunnel;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;

/// <summary>
/// Pure managed userspace IPv6/TCP stack running directly over a <see cref="CoreDeviceProxyClient"/> L3 tunnel.
/// Eliminates the need for root privileges or OS virtual network drivers (utun/wintun).
/// </summary>
public sealed class UserspaceTcpTunnel : IAsyncDisposable
{
    private const byte NextHeaderTcp = 6;
    private const byte TcpFlagFin = 0x01;
    private const byte TcpFlagSyn = 0x02;
    private const byte TcpFlagRst = 0x04;
    private const byte TcpFlagPsh = 0x08;
    private const byte TcpFlagAck = 0x10;

    private readonly CoreDeviceProxyClient proxy;
    private readonly byte[] clientAddrBytes;
    private readonly byte[] serverAddrBytes;
    private readonly int maxSegmentPayload;
    private readonly ConcurrentDictionary<(ushort LocalPort, ushort RemotePort), UserspaceTcpStream> connections = new();
    private readonly CancellationTokenSource pumpCts = new();
    private readonly Task pumpTask;
    private int nextEphemeralPort = 50000;
    private bool disposed;

    public UserspaceTcpTunnel(CoreDeviceProxyClient proxy)
    {
        this.proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        clientAddrBytes = proxy.Parameters.ClientAddress.GetAddressBytes();
        serverAddrBytes = proxy.Parameters.ServerAddress.GetAddressBytes();
        if (clientAddrBytes.Length != 16 || serverAddrBytes.Length != 16)
        {
            throw new ArgumentException("CoreDeviceProxy tunnel addresses must be IPv6.");
        }

        // Leave room for IPv6 (40) + TCP (20) headers
        maxSegmentPayload = Math.Clamp(proxy.Parameters.Mtu - 60, 1200, 14000);
        pumpTask = Task.Run(() => ReceiveLoopAsync(pumpCts.Token));
    }

    public CoreDeviceTunnelParameters Parameters => proxy.Parameters;

    public async Task<Stream> ConnectAsync(ushort remotePort, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        ushort localPort = (ushort)(Interlocked.Increment(ref nextEphemeralPort) & 0x7FFF | 0x8000);
        var stream = new UserspaceTcpStream(this, localPort, remotePort, maxSegmentPayload);
        if (!connections.TryAdd((localPort, remotePort), stream))
        {
            throw new InvalidOperationException($"Duplicate local ephemeral port {localPort}.");
        }

        try
        {
            await stream.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            connections.TryRemove((localPort, remotePort), out _);
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal void Unregister(ushort localPort, ushort remotePort)
    {
        connections.TryRemove((localPort, remotePort), out _);
    }

    internal Task SendTcpSegmentAsync(
        ushort srcPort,
        ushort dstPort,
        uint seq,
        uint ack,
        byte flags,
        ushort window,
        ReadOnlySpan<byte> options,
        ReadOnlySpan<byte> payload,
        CancellationToken cancellationToken)
    {
        int tcpHeaderLen = 20 + options.Length;
        if ((tcpHeaderLen & 3) != 0)
        {
            throw new ArgumentException("TCP options length must be a multiple of 4 bytes.");
        }

        int tcpTotalLen = tcpHeaderLen + payload.Length;
        byte[] packet = new byte[40 + tcpTotalLen];

        // IPv6 header (40 bytes)
        packet[0] = 0x60; // Version 6, traffic class 0
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4, 2), (ushort)tcpTotalLen);
        packet[6] = NextHeaderTcp;
        packet[7] = 64; // Hop limit
        clientAddrBytes.CopyTo(packet.AsSpan(8, 16));
        serverAddrBytes.CopyTo(packet.AsSpan(24, 16));

        // TCP header
        Span<byte> tcp = packet.AsSpan(40);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(0, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(8, 4), ack);
        tcp[12] = (byte)((tcpHeaderLen / 4) << 4);
        tcp[13] = flags;
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(14, 2), window);
        // checksum at 16..18, urgent ptr at 18..20
        if (!options.IsEmpty)
        {
            options.CopyTo(tcp.Slice(20));
        }

        if (!payload.IsEmpty)
        {
            payload.CopyTo(tcp.Slice(tcpHeaderLen));
        }

        ushort checksum = ComputeIpv6TcpChecksum(clientAddrBytes, serverAddrBytes, tcp);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(16, 2), checksum);

        return proxy.WritePacketAsync(packet, cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? packet = await proxy.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (packet == null)
                {
                    break;
                }

                ProcessIncomingPacket(packet);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            foreach (var conn in connections.Values)
            {
                conn.OnTransportFault(ex);
            }
        }
        finally
        {
            foreach (var conn in connections.Values)
            {
                conn.OnTransportClosed();
            }
        }
    }

    private void ProcessIncomingPacket(byte[] packet)
    {
        if (packet.Length < 60 || packet[6] != NextHeaderTcp)
        {
            return;
        }

        ReadOnlySpan<byte> tcp = packet.AsSpan(40);
        ushort remotePort = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(0, 2));
        ushort localPort = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(2, 2));
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(tcp.Slice(4, 4));
        uint ack = BinaryPrimitives.ReadUInt32BigEndian(tcp.Slice(8, 4));
        int dataOffset = (tcp[12] >> 4) * 4;
        byte flags = tcp[13];
        ushort window = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(14, 2));

        if (dataOffset < 20 || 40 + dataOffset > packet.Length)
        {
            return;
        }

        if (connections.TryGetValue((localPort, remotePort), out var conn))
        {
            byte[] payload = packet.Length > 40 + dataOffset
                ? packet.AsSpan(40 + dataOffset).ToArray()
                : Array.Empty<byte>();
            conn.OnSegmentReceived(seq, ack, flags, window, payload);
        }
    }

    internal static ushort ComputeIpv6TcpChecksum(ReadOnlySpan<byte> srcAddr, ReadOnlySpan<byte> dstAddr, ReadOnlySpan<byte> tcpSegment)
    {
        uint sum = 0;
        AddChecksumWords(ref sum, srcAddr);
        AddChecksumWords(ref sum, dstAddr);

        Span<byte> pseudoTail = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(pseudoTail.Slice(0, 4), (uint)tcpSegment.Length);
        pseudoTail[4] = 0;
        pseudoTail[5] = 0;
        pseudoTail[6] = 0;
        pseudoTail[7] = NextHeaderTcp;
        AddChecksumWords(ref sum, pseudoTail);
        AddChecksumWords(ref sum, tcpSegment);

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static void AddChecksumWords(ref uint sum, ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 1 < data.Length)
        {
            sum += (uint)((data[i] << 8) | data[i + 1]);
            i += 2;
        }

        if (i < data.Length)
        {
            sum += (uint)(data[i] << 8);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        pumpCts.Cancel();
        foreach (var conn in connections.Values)
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }

        connections.Clear();
        await proxy.DisposeAsync().ConfigureAwait(false);
        try
        {
            await pumpTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore shutdown errors.
        }

        pumpCts.Dispose();
    }

    private sealed class UserspaceTcpStream : Stream
    {
        private readonly UserspaceTcpTunnel tunnel;
        private readonly ushort localPort;
        private readonly ushort remotePort;
        private readonly int mss;
        private readonly object stateLock = new();
        private readonly SemaphoreSlim writeLock = new(1, 1);
        private readonly SortedDictionary<uint, byte[]> outOfOrder = new();
        private readonly Channel<byte[]> inbound = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        private readonly TaskCompletionSource synAckTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private uint sendNext;
        private uint recvNext;
        private bool established;
        private bool remoteFin;
        private bool closed;
        private byte[]? currentReadChunk;
        private int currentReadOffset;

        public UserspaceTcpStream(UserspaceTcpTunnel tunnel, ushort localPort, ushort remotePort, int mss)
        {
            this.tunnel = tunnel;
            this.localPort = localPort;
            this.remotePort = remotePort;
            this.mss = mss;
            sendNext = (uint)Random.Shared.Next(100000, int.MaxValue);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public async Task HandshakeAsync(CancellationToken cancellationToken)
        {
            byte[] synOptions = new byte[4];
            synOptions[0] = 2; // MSS option kind
            synOptions[1] = 4; // Length
            BinaryPrimitives.WriteUInt16BigEndian(synOptions.AsSpan(2, 2), (ushort)Math.Min(mss, 65535));

            uint isn;
            lock (stateLock)
            {
                isn = sendNext;
                sendNext++;
            }

            await tunnel.SendTcpSegmentAsync(localPort, remotePort, isn, 0, TcpFlagSyn, 65535, synOptions, ReadOnlySpan<byte>.Empty, cancellationToken).ConfigureAwait(false);

            using var reg = cancellationToken.Register(() => synAckTcs.TrySetCanceled(cancellationToken));
            await synAckTcs.Task.ConfigureAwait(false);

            // Send and await the final 3-way handshake ACK before returning the connected stream
            uint ackSeq;
            uint ackNum;
            lock (stateLock)
            {
                ackSeq = sendNext;
                ackNum = recvNext;
            }

            await tunnel.SendTcpSegmentAsync(localPort, remotePort, ackSeq, ackNum, TcpFlagAck, 65535, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, cancellationToken).ConfigureAwait(false);
        }

        public void OnSegmentReceived(uint seq, uint ack, byte flags, ushort window, byte[] payload)
        {
            bool sendAckNow = false;
            uint ackToSend = 0;
            uint seqToSend = 0;

            lock (stateLock)
            {
                if ((flags & TcpFlagRst) != 0)
                {
                    var ex = new IOException($"TCP connection reset by peer (port {remotePort}).");
                    synAckTcs.TrySetException(ex);
                    inbound.Writer.TryComplete(ex);
                    closed = true;
                    return;
                }

                if (!established)
                {
                    if ((flags & (TcpFlagSyn | TcpFlagAck)) == (TcpFlagSyn | TcpFlagAck))
                    {
                        recvNext = seq + 1;
                        established = true;
                        synAckTcs.TrySetResult();
                    }

                    return;
                }

                if (payload.Length > 0)
                {
                    int delta = unchecked((int)(seq - recvNext));
                    if (delta == 0)
                    {
                        inbound.Writer.TryWrite(payload);
                        recvNext += (uint)payload.Length;
                        DrainOutOfOrderLocked();
                        sendAckNow = true;
                    }
                    else if (delta > 0)
                    {
                        outOfOrder[seq] = payload;
                        sendAckNow = true;
                    }
                    else
                    {
                        // Duplicate or partially overlapping segment
                        int overlap = -delta;
                        if (overlap < payload.Length)
                        {
                            byte[] fresh = payload.AsSpan(overlap).ToArray();
                            inbound.Writer.TryWrite(fresh);
                            recvNext += (uint)fresh.Length;
                            DrainOutOfOrderLocked();
                        }
                        sendAckNow = true;
                    }
                }

                if ((flags & TcpFlagFin) != 0 && !remoteFin)
                {
                    // Account for FIN sequence number if it aligns with recvNext
                    uint finSeq = seq + (uint)payload.Length;
                    if (finSeq == recvNext)
                    {
                        recvNext++;
                        remoteFin = true;
                        inbound.Writer.TryComplete();
                        sendAckNow = true;
                    }
                }

                seqToSend = sendNext;
                ackToSend = recvNext;
            }

            if (sendAckNow)
            {
                _ = tunnel.SendTcpSegmentAsync(localPort, remotePort, seqToSend, ackToSend, TcpFlagAck, 65535, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, CancellationToken.None);
            }
        }

        private void DrainOutOfOrderLocked()
        {
            while (outOfOrder.Count > 0)
            {
                uint firstSeq = outOfOrder.Keys.First();
                int delta = unchecked((int)(firstSeq - recvNext));
                if (delta > 0)
                {
                    break;
                }

                byte[] seg = outOfOrder[firstSeq];
                outOfOrder.Remove(firstSeq);

                if (delta == 0)
                {
                    inbound.Writer.TryWrite(seg);
                    recvNext += (uint)seg.Length;
                }
                else
                {
                    int overlap = -delta;
                    if (overlap < seg.Length)
                    {
                        byte[] fresh = seg.AsSpan(overlap).ToArray();
                        inbound.Writer.TryWrite(fresh);
                        recvNext += (uint)fresh.Length;
                    }
                }
            }
        }

        public void OnTransportFault(Exception ex)
        {
            synAckTcs.TrySetException(ex);
            inbound.Writer.TryComplete(ex);
        }

        public void OnTransportClosed()
        {
            synAckTcs.TrySetException(new EndOfStreamException("CoreDeviceProxy tunnel closed before TCP handshake completed."));
            inbound.Writer.TryComplete();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            while (currentReadChunk == null || currentReadOffset >= currentReadChunk.Length)
            {
                if (!await inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }

                if (inbound.Reader.TryRead(out byte[]? next))
                {
                    currentReadChunk = next;
                    currentReadOffset = 0;
                }
            }

            int available = currentReadChunk.Length - currentReadOffset;
            int toCopy = Math.Min(available, buffer.Length);
            currentReadChunk.AsSpan(currentReadOffset, toCopy).CopyTo(buffer.Span);
            currentReadOffset += toCopy;
            return toCopy;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int chunkLen = Math.Min(mss, buffer.Length - offset);
                    uint seq;
                    uint ack;
                    lock (stateLock)
                    {
                        if (closed)
                        {
                            throw new IOException("TCP stream is closed or faulted.");
                        }

                        seq = sendNext;
                        ack = recvNext;
                    }

                    try
                    {
                        await tunnel.SendTcpSegmentAsync(
                            localPort,
                            remotePort,
                            seq,
                            ack,
                            (byte)(TcpFlagPsh | TcpFlagAck),
                            65535,
                            ReadOnlySpan<byte>.Empty,
                            buffer.Span.Slice(offset, chunkLen),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // If a write was cancelled or failed mid-stream, mark stream closed to prevent sequence gaps
                        lock (stateLock)
                        {
                            closed = true;
                        }

                        throw;
                    }

                    lock (stateLock)
                    {
                        sendNext += (uint)chunkLen;
                    }

                    offset += chunkLen;
                }
            }
            finally
            {
                writeLock.Release();
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync()
        {
            bool sendFin = false;
            uint seq = 0;
            uint ack = 0;
            lock (stateLock)
            {
                if (!closed)
                {
                    closed = true;
                    if (established)
                    {
                        seq = sendNext++;
                        ack = recvNext;
                        sendFin = true;
                    }
                }
            }

            if (sendFin)
            {
                try
                {
                    await tunnel.SendTcpSegmentAsync(localPort, remotePort, seq, ack, (byte)(TcpFlagFin | TcpFlagAck), 65535, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore FIN errors on closing tunnel.
                }
            }

            inbound.Writer.TryComplete();
            tunnel.Unregister(localPort, remotePort);
            writeLock.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
