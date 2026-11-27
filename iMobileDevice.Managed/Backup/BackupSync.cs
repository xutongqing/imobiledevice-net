namespace iMobileDevice.Managed.Backup;

using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

internal sealed class BackupSync(AfcClient afc, Stream notifications) : IAsyncDisposable
{
    private readonly BackupWire proxy = new(notifications);
    private ulong? handle;
    private bool started;
    public AfcClient Afc => afc;

    public static async Task<BackupSync> AcquireAsync(LockdownClient lockdown, CancellationToken token)
    {
        var afc = await AfcClient.ConnectAsync(lockdown, token).ConfigureAwait(false);
        Stream notifications;
        try { notifications = await lockdown.ConnectToServiceAsync("com.apple.mobile.notification_proxy", cancellationToken: token).ConfigureAwait(false); }
        catch { await afc.DisposeAsync().ConfigureAwait(false); throw; }
        var sync = new BackupSync(afc, notifications);
        try
        {
            await sync.NotifyAsync("syncWillStart", token).ConfigureAwait(false);
            sync.started = true;
            sync.handle = await afc.OpenFileHandleAsync("/com.apple.itunes.lock_sync", AfcFileOpenMode.ReadWrite, token).ConfigureAwait(false);
            await sync.NotifyAsync("syncLockRequest", token).ConfigureAwait(false);
            for (int attempt = 1; ; attempt++)
            {
                try { await afc.SetBackupLockAsync(sync.handle.Value, true, token).ConfigureAwait(false); break; }
                catch (AfcException ex) when (ex.ErrorCode == AfcErrorCode.OpWouldBlock && attempt < 50)
                { await Task.Delay(200, token).ConfigureAwait(false); }
            }
            await sync.NotifyAsync("syncDidStart", token).ConfigureAwait(false);
            return sync;
        }
        catch { await sync.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private Task NotifyAsync(string name, CancellationToken token) =>
        proxy.SendAsync(new PlistDictionary { ["Command"] = "PostNotification", ["Name"] = "com.apple.itunes-mobdev." + name }, token);

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            if (handle.HasValue)
            {
                await afc.SetBackupLockAsync(handle.Value, false, deadline.Token).ConfigureAwait(false);
                await afc.CloseFileHandleAsync(handle.Value, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or AfcException or ObjectDisposedException) { }
        finally { await afc.DisposeAsync().ConfigureAwait(false); }
        try { if (started) await NotifyAsync("syncDidFinish", deadline.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { await notifications.DisposeAsync().ConfigureAwait(false); }
    }
}
