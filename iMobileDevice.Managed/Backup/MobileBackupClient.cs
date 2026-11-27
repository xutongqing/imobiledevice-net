namespace iMobileDevice.Managed.Backup;

using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

public sealed class MobileBackupException(long errorCode, string? description)
    : IOException($"MobileBackup2 error {errorCode}: {description ?? "No description"}")
{
    public long ErrorCode { get; } = errorCode;
}

public sealed class BackupRestoreOptions
{
    public string? SourceIdentifier { get; init; }
    public string? Password { get; init; }
    public bool RestoreSystemFiles { get; init; }
    public bool Reboot { get; init; } = true;
    public bool PreserveSettings { get; init; } = true;
    public bool RemoveItemsNotRestored { get; init; }
    public bool ReinstallApplications { get; init; } = true;
}

/// <summary>Host for the paired USB/Wi-Fi Lockdown MobileBackup2 service. The caller owns the Lockdown session.</summary>
public sealed class MobileBackupClient
{
    public const string ServiceName = "com.apple.mobilebackup2";
    private readonly LockdownClient lockdown;

    public MobileBackupClient(LockdownClient lockdown) => this.lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
    private string Target => lockdown.Device.SerialNumber;

    public async Task<bool> GetWillEncryptAsync(CancellationToken cancellationToken = default) =>
        (await lockdown.GetValueAsync("com.apple.mobile.backup", "WillEncrypt", cancellationToken).ConfigureAwait(false))?.AsBoolean()
        ?? throw new InvalidDataException("The device did not report its backup encryption setting.");

    /// <summary>Creates a full or incremental backup and returns the published device directory.
    /// An incremental request with no existing snapshot starts a full backup.</summary>
    public async Task<string> BackupAsync(string directory, bool full = true, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var store = new BackupStorage(directory, Target);
        if (!full && Directory.Exists(store.Snapshot))
        {
            BackupStorage.ValidateSnapshot(store.Snapshot);
            await BackupStorage.CopyAsync(store.Snapshot, store.WorkingSnapshot, cancellationToken).ConfigureAwait(false);
        }
        else full = true;

        BackupStorage.CreatePrivateDirectory(store.WorkingSnapshot);
        await using var stream = await lockdown.ConnectToServiceAsync(ServiceName, withEscrowBag: true, cancellationToken).ConfigureAwait(false);
        var link = new DeviceLink(stream, store.WorkRoot);
        await link.HandshakeAsync(cancellationToken).ConfigureAwait(false);
        await using var sync = await BackupSync.AcquireAsync(lockdown, cancellationToken).ConfigureAwait(false);
        var info = await BackupInfo.CreateAsync(lockdown, sync.Afc, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(BackupStorage.Resolve(store.WorkingSnapshot, "Info.plist"),
            PlistSerializer.WriteXml(info), cancellationToken).ConfigureAwait(false);
        if (full)
        {
            var status = new PlistDictionary
            {
                ["BackupState"] = "new", ["Date"] = DateTimeOffset.UtcNow, ["IsFullBackup"] = true,
                ["Version"] = "3.3", ["SnapshotState"] = "finished", ["UUID"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            };
            await File.WriteAllBytesAsync(BackupStorage.Resolve(store.WorkingSnapshot, "Status.plist"),
                PlistSerializer.WriteBinary(status), cancellationToken).ConfigureAwait(false);
            File.Create(BackupStorage.Resolve(store.WorkingSnapshot, "Manifest.plist")).Dispose();
        }
        var request = Request("Backup", Target);
        if (full) request["Options"] = new PlistDictionary { ["ForceFullBackup"] = true };
        await link.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
        await link.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        store.Publish();
        await FinishAsync(link).ConfigureAwait(false);
        return store.Snapshot;
    }

    /// <summary>Restores a finished snapshot to the connected device. This changes device data and can reboot it.</summary>
    public async Task RestoreAsync(string directory, BackupRestoreOptions? options = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        string source = options.SourceIdentifier ?? Target;
        using var store = new BackupStorage(directory, source);
        var manifest = BackupStorage.ValidateSnapshot(store.Snapshot);
        var request = RestoreRequest(Target, source, manifest, options);
        await BackupStorage.CopyAsync(store.Snapshot, store.WorkingSnapshot, cancellationToken).ConfigureAwait(false);
        BackupStorage.ValidateIdentifier(Target);
        Directory.CreateDirectory(BackupStorage.Resolve(store.WorkRoot, Target));
        await using var stream = await lockdown.ConnectToServiceAsync(ServiceName, withEscrowBag: true, cancellationToken).ConfigureAwait(false);
        var link = new DeviceLink(stream, store.WorkRoot);
        await link.HandshakeAsync(cancellationToken).ConfigureAwait(false);
        await using var sync = await BackupSync.AcquireAsync(lockdown, cancellationToken).ConfigureAwait(false);
        var apps = options.ReinstallApplications ? BackupStorage.ReadMetadata(store.WorkingSnapshot, "Info.plist").GetDictionary("Applications") : null;
        await link.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
        await BackupInfo.SetRestoreAppsAsync(sync.Afc, apps, cancellationToken).ConfigureAwait(false);
        await link.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        await FinishAsync(link).ConfigureAwait(false);
    }

    internal static PlistDictionary RestoreRequest(string target, string source, PlistDictionary manifest, BackupRestoreOptions options)
    {
        var settings = new PlistDictionary
        {
            ["RestoreShouldReboot"] = options.Reboot, ["RestoreDontCopyBackup"] = true, // caller operates on a private copy
            ["RestorePreserveSettings"] = options.PreserveSettings, ["RestoreSystemFiles"] = options.RestoreSystemFiles,
            ["RemoveItemsNotRestored"] = options.RemoveItemsNotRestored,
        };
        if (manifest.GetBoolean("IsEncrypted") == true)
        {
            if (string.IsNullOrEmpty(options.Password)) throw new ArgumentException("The encrypted backup requires its password.", nameof(options));
            settings["Password"] = options.Password;
        }
        return new PlistDictionary { ["MessageName"] = "Restore", ["TargetIdentifier"] = target, ["SourceIdentifier"] = source, ["Options"] = settings };
    }

    /// <summary>Enables, changes, or disables device backup encryption. Supply the existing password when set.</summary>
    public async Task ChangePasswordAsync(string? oldPassword, string? newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(oldPassword) && string.IsNullOrEmpty(newPassword))
            throw new ArgumentException("Provide the current or new password.");
        var request = Request("ChangePassword");
        if (!string.IsNullOrEmpty(oldPassword)) request["OldPassword"] = oldPassword;
        if (!string.IsNullOrEmpty(newPassword)) request["NewPassword"] = newPassword;
        string temporary = Directory.CreateTempSubdirectory("imobile-backup-").FullName;
        try { await ExecuteAsync(temporary, request, cancellationToken).ConfigureAwait(false); }
        finally { Directory.Delete(temporary, true); }
    }

    /// <summary>Requests the device's CSV listing of a backup manifest.</summary>
    public Task<string> ListAsync(string directory, string? sourceIdentifier = null, string? password = null,
        CancellationToken cancellationToken = default) => DescribeAsync("List", directory, sourceIdentifier, password, cancellationToken);

    /// <summary>Returns the device's description of a saved backup.</summary>
    public Task<string> GetInfoAsync(string directory, string? sourceIdentifier = null, string? password = null,
        CancellationToken cancellationToken = default) => DescribeAsync("Info", directory, sourceIdentifier, password, cancellationToken);

    private async Task<string> DescribeAsync(string command, string directory, string? sourceIdentifier, string? password,
        CancellationToken cancellationToken)
    {
        string source = sourceIdentifier ?? Target;
        using var store = new BackupStorage(directory, source);
        await BackupManifest.PrepareListAsync(store, password, cancellationToken).ConfigureAwait(false);
        var result = await ExecuteAsync(store.WorkRoot, Request(command, source), cancellationToken).ConfigureAwait(false);
        string text = result?.AsString() ?? throw new InvalidDataException($"The device returned no content for {command}.");
        if (command == "Info" && BackupStorage.ReadMetadata(store.Snapshot, "Manifest.plist").GetBoolean("IsEncrypted") == true)
            text = text.Replace("encrypted=0", "encrypted=1", StringComparison.Ordinal);
        return text;
    }

    /// <summary>Reads the Info.plist of a local finished backup.</summary>
    public static PlistDictionary ReadInfo(string directory, string sourceIdentifier)
    {
        using var store = new BackupStorage(directory, sourceIdentifier);
        BackupStorage.ValidateSnapshot(store.Snapshot);
        return BackupStorage.ReadMetadata(store.Snapshot, "Info.plist");
    }

    /// <summary>Copies a regular file from an unencrypted iTunes-format backup to the supplied stream.</summary>
    public static async Task ExtractFileAsync(string directory, string sourceIdentifier, string domain, string relativePath,
        Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(destination);
        using var store = new BackupStorage(directory, sourceIdentifier);
        if (BackupStorage.ValidateSnapshot(store.Snapshot).GetBoolean("IsEncrypted") == true)
            throw new NotSupportedException("Direct file extraction requires an unencrypted backup.");
        byte[] digest = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(domain + "-" + relativePath));
        string id = Convert.ToHexString(digest).ToLowerInvariant();
        string path = BackupStorage.Resolve(store.Snapshot, id[..2] + "/" + id);
        if (!File.Exists(path)) path = BackupStorage.Resolve(store.Snapshot, id);
        await using var input = File.OpenRead(path);
        await input.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Extracts one backup file via MobileBackup2, including password-protected backups.</summary>
    public async Task ExtractAsync(string directory, string domain, string relativePath, Stream destination,
        string? sourceIdentifier = null, string? password = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(destination);
        string source = sourceIdentifier ?? Target;
        using var store = new BackupStorage(directory, source);
        var request = Request("Extract", source);
        request["DomainName"] = domain;
        request["RelativePath"] = relativePath;
        await ExportAsync(store, request, password, cancellationToken).ConfigureAwait(false);
        var files = Directory.EnumerateFileSystemEntries(store.WorkRoot)
            .Where(path => path != store.WorkingSnapshot)
            .SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : new[] { path })
            .Take(2).ToList();
        if (files.Count != 1) throw new InvalidDataException("The device did not return exactly one extracted file.");
        await using var input = File.OpenRead(files[0]);
        await input.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unpacks the backup with the device and returns a new sibling directory containing the exported tree.</summary>
    public async Task<string> UnpackAsync(string directory, string? sourceIdentifier = null, string? password = null,
        CancellationToken cancellationToken = default)
    {
        string source = sourceIdentifier ?? Target;
        using var store = new BackupStorage(directory, source);
        await ExportAsync(store, Request("Unback", source), password, cancellationToken).ConfigureAwait(false);
        string output = BackupStorage.Resolve(Path.GetDirectoryName(store.Snapshot)!, source + ".unback-" + Guid.NewGuid().ToString("N"));
        Directory.Move(BackupStorage.Resolve(store.WorkRoot, "_unback_"), output);
        return output;
    }

    private async Task ExportAsync(BackupStorage store, PlistDictionary request, string? password, CancellationToken token)
    {
        var manifest = BackupStorage.ValidateSnapshot(store.Snapshot);
        if (manifest.GetBoolean("IsEncrypted") == true)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("The encrypted backup requires its password.", nameof(password));
            request["Password"] = password;
        }
        await BackupStorage.CopyAsync(store.Snapshot, store.WorkingSnapshot, token).ConfigureAwait(false);
        await ExecuteAsync(store.WorkRoot, request, token).ConfigureAwait(false);
    }

    private PlistDictionary Request(string name, string? source = null)
    {
        var request = new PlistDictionary { ["MessageName"] = name, ["TargetIdentifier"] = Target };
        if (source != null) request["SourceIdentifier"] = source;
        return request;
    }

    private async Task<PlistNode?> ExecuteAsync(string root, PlistDictionary request, CancellationToken token)
    {
        await using var stream = await lockdown.ConnectToServiceAsync(ServiceName, withEscrowBag: true, token).ConfigureAwait(false);
        var link = new DeviceLink(stream, root);
        await link.HandshakeAsync(token).ConfigureAwait(false);
        await link.ProcessAsync(request, token).ConfigureAwait(false);
        var result = await link.RunAsync(null, token).ConfigureAwait(false);
        await FinishAsync(link).ConfigureAwait(false);
        return result;
    }

    private static async Task FinishAsync(DeviceLink link)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await link.DisconnectAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
}
