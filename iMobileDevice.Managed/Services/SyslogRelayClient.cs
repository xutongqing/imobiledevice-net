namespace iMobileDevice.Managed.Services;

using System.Runtime.CompilerServices;
using System.Text;
using iMobileDevice.Managed.Lockdown;

/// <summary>
/// Pure managed streaming client for <c>com.apple.syslog_relay</c>.
/// </summary>
public sealed class SyslogRelayClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.syslog_relay";

    private readonly Stream stream;
    private bool disposed;

    public SyslogRelayClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public static async Task<SyslogRelayClient> ConnectAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new SyslogRelayClient(serviceStream);
    }

    /// <summary>
    /// Streams decoded syslog entries from the device until cancelled or EOF.
    /// </summary>
    public async IAsyncEnumerable<string> WatchLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        byte[] readBuf = new byte[4096];
        using var lineBuf = new MemoryStream(1024);

        while (!cancellationToken.IsCancellationRequested)
        {
            int n = await stream.ReadAsync(readBuf, cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                byte b = readBuf[i];
                if (b == 0 || b == (byte)'\n')
                {
                    if (lineBuf.Length > 0)
                    {
                        string entry = DecodeLine(lineBuf);
                        lineBuf.SetLength(0);
                        if (entry.Length > 0)
                        {
                            yield return entry;
                        }
                    }
                }
                else if (b != (byte)'\r')
                {
                    lineBuf.WriteByte(b);
                }
            }
        }

        if (lineBuf.Length > 0 && !cancellationToken.IsCancellationRequested)
        {
            string tail = DecodeLine(lineBuf);
            if (tail.Length > 0)
            {
                yield return tail;
            }
        }
    }

    private static string DecodeLine(MemoryStream ms)
    {
        if (!ms.TryGetBuffer(out ArraySegment<byte> seg))
        {
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        return Encoding.UTF8.GetString(seg.Array!, seg.Offset, seg.Count);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await stream.DisposeAsync().ConfigureAwait(false);
    }
}
