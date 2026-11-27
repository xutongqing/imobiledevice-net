namespace iMobileDevice.Managed.Services;

using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;

/// <summary>
/// Pure managed client for reading, pulling, and clearing device crash reports via
/// <c>com.apple.crashreportcopymobile</c> (and optional <c>com.apple.crashreportmover</c> flush).
/// </summary>
public sealed class CrashReportsClient : IAsyncDisposable
{
    public const string CopyServiceName = "com.apple.crashreportcopymobile";
    public const string MoverServiceName = "com.apple.crashreportmover";

    private readonly AfcClient afc;

    public CrashReportsClient(AfcClient afcClient)
    {
        afc = afcClient ?? throw new ArgumentNullException(nameof(afcClient));
    }

    public static async Task<CrashReportsClient> ConnectAsync(
        LockdownClient lockdown,
        bool flushPendingLogs = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);

        if (flushPendingLogs)
        {
            await TriggerCrashReportMoverAsync(lockdown, cancellationToken).ConfigureAwait(false);
        }

        Stream serviceStream = await lockdown.ConnectToServiceAsync(CopyServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new CrashReportsClient(new AfcClient(serviceStream));
    }

    public static async Task TriggerCrashReportMoverAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        await using Stream moverStream = await lockdown.ConnectToServiceAsync(MoverServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        byte[] ping = new byte[4];
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            _ = await moverStream.ReadAsync(ping, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Mover closes or pings once done; timeout is non-fatal.
        }
    }

    public AfcClient Afc => afc;

    public async Task<IReadOnlyList<AfcDirectoryEntry>> ListAsync(
        string path = "/",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<AfcDirectoryEntry>();
        await foreach (var entry in afc.EnumerateDirectoryAsync(path, new AfcListOptions { Recursive = recursive }, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        return entries;
    }

    public Task<AfcTransferResult> PullAsync(
        string remotePath,
        string localDestinationPath,
        AfcPullOptions? options = null,
        IProgress<AfcTransferProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        afc.PullAsync(remotePath, localDestinationPath, options, progress, cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> rootEntries = await afc.ReadDirectoryAsync("/", cancellationToken).ConfigureAwait(false);
        foreach (string entry in rootEntries)
        {
            if (entry is "." or "..")
            {
                continue;
            }

            string fullPath = "/" + entry;
            try
            {
                await afc.RemovePathAndContentsAsync(fullPath, cancellationToken).ConfigureAwait(false);
            }
            catch (AfcException ex) when (ex.ErrorCode is AfcErrorCode.PermDenied or AfcErrorCode.ObjectNotFound)
            {
                // Some system subdirectories under CrashReporter are protected on newer iOS; skip gracefully.
            }
        }
    }

    public ValueTask DisposeAsync() => afc.DisposeAsync();
}
