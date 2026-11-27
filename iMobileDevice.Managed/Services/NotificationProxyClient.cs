namespace iMobileDevice.Managed.Services;

using System.Runtime.CompilerServices;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Standard Darwin notification names accepted by <c>com.apple.mobile.notification_proxy</c>.
/// </summary>
public static class NotificationProxyNames
{
    public const string SyncWillStart = "com.apple.itunes-mobdev.syncWillStart";
    public const string SyncDidStart = "com.apple.itunes-mobdev.syncDidStart";
    public const string SyncDidFinish = "com.apple.itunes-mobdev.syncDidFinish";
    public const string SyncLockRequest = "com.apple.itunes-mobdev.syncLockRequest";
    public const string SyncCancelRequest = "com.apple.itunes-client.syncCancelRequest";
    public const string SyncSuspendRequest = "com.apple.itunes-client.syncSuspendRequest";
    public const string SyncResumeRequest = "com.apple.itunes-client.syncResumeRequest";
    public const string PhoneNumberChanged = "com.apple.mobile.lockdown.phone_number_changed";
    public const string DeviceNameChanged = "com.apple.mobile.lockdown.device_name_changed";
    public const string TimezoneChanged = "com.apple.mobile.lockdown.timezone_changed";
    public const string TrustedHostAttached = "com.apple.mobile.lockdown.trusted_host_attached";
    public const string HostDetached = "com.apple.mobile.lockdown.host_detached";
    public const string HostAttached = "com.apple.mobile.lockdown.host_attached";
    public const string RegistrationFailed = "com.apple.mobile.lockdown.registration_failed";
    public const string ActivationState = "com.apple.mobile.lockdown.activation_state";
    public const string BrickState = "com.apple.mobile.lockdown.brick_state";
    public const string DiskUsageChanged = "com.apple.mobile.lockdown.disk_usage_changed";
    public const string DsDomainChanged = "com.apple.mobile.data_sync.domain_changed";
    public const string BackupDomainChanged = "com.apple.mobile.backup.domain_changed";
    public const string AppInstalled = "com.apple.mobile.application_installed";
    public const string AppUninstalled = "com.apple.mobile.application_uninstalled";
    public const string DevImageMounted = "com.apple.mobile.developer_image_mounted";
    public const string AttemptActivation = "com.apple.springboard.attemptactivation";
    public const string ItdbPrepDidEnd = "com.apple.itdbprep.notification.didEnd";
    public const string LanguageChanged = "com.apple.language.changed";
    public const string AddressBookPrefChanged = "com.apple.AddressBook.PreferenceChanged";
}

/// <summary>
/// Pure managed client for <c>com.apple.mobile.notification_proxy</c>.
/// </summary>
public sealed class NotificationProxyClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.mobile.notification_proxy";
    public const string InsecureServiceName = "com.apple.mobile.insecure_notification_proxy";

    private readonly Stream stream;
    private readonly PlistServiceWire wire;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private bool disposed;

    public NotificationProxyClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        wire = new PlistServiceWire(stream, PlistFormat.Xml);
    }

    public static async Task<NotificationProxyClient> ConnectAsync(
        LockdownClient lockdown,
        bool insecure = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        string service = insecure ? InsecureServiceName : ServiceName;
        Stream serviceStream = await lockdown.ConnectToServiceAsync(service, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new NotificationProxyClient(serviceStream);
    }

    public async Task ObserveAsync(string notificationName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationName);
        ObjectDisposedException.ThrowIf(disposed, this);

        var req = new PlistDictionary
        {
            ["Command"] = "ObserveNotification",
            ["Name"] = notificationName,
        };

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await wire.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task ObserveAsync(IEnumerable<string> notificationNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificationNames);
        foreach (string name in notificationNames)
        {
            await ObserveAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task PostAsync(string notificationName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationName);
        ObjectDisposedException.ThrowIf(disposed, this);

        var req = new PlistDictionary
        {
            ["Command"] = "PostNotification",
            ["Name"] = notificationName,
        };

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await wire.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await wire.SendAsync(new PlistDictionary { ["Command"] = "Shutdown" }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Reads relayed notifications from the device until <c>ProxyDeath</c>, EOF, or cancellation.
    /// </summary>
    public async IAsyncEnumerable<string> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        while (!cancellationToken.IsCancellationRequested)
        {
            PlistNode node;
            try
            {
                node = await wire.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                yield break;
            }

            if (node is not PlistDictionary dict)
            {
                continue;
            }

            string? command = dict.GetString("Command");
            if (string.Equals(command, "ProxyDeath", StringComparison.Ordinal))
            {
                yield break;
            }

            if (string.Equals(command, "RelayNotification", StringComparison.Ordinal) &&
                dict.GetString("Name") is { Length: > 0 } name)
            {
                yield return name;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await stream.DisposeAsync().ConfigureAwait(false);
        writeLock.Dispose();
    }
}
