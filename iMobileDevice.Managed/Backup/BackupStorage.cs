namespace iMobileDevice.Managed.Backup;

using iMobileDevice.Managed.Plist;

/// <summary>Paths and transactions for a local iTunes-format backup directory.</summary>
internal sealed class BackupStorage : IDisposable
{
    private readonly FileStream lease;
    private readonly string previous;
    public string Snapshot { get; }
    public string WorkRoot { get; }
    public string WorkingSnapshot { get; }

    public BackupStorage(string directory, string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ValidateIdentifier(identifier);
        string root = Path.GetFullPath(directory);
        CreatePrivateDirectory(root);
        Snapshot = Resolve(root, identifier);
        string state = Resolve(root, ".imobiledevice-managed/" + identifier);
        CreatePrivateDirectory(state);
        lease = new FileStream(Resolve(state, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            previous = Resolve(state, "previous");
            WorkRoot = Resolve(state, "work");
            // Recover a publication interrupted between the two same-volume renames.
            if (Directory.Exists(previous))
            {
                if (Directory.Exists(Snapshot)) Directory.Delete(previous, true);
                else Directory.Move(previous, Snapshot);
            }
            if (Directory.Exists(WorkRoot)) Directory.Delete(WorkRoot, true);
            CreatePrivateDirectory(WorkRoot);
            WorkingSnapshot = Resolve(WorkRoot, identifier);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void ValidateIdentifier(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!char.IsAsciiLetterOrDigit(id[0]) || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException("Invalid backup identifier.", nameof(id));
        ValidateComponent(id);
    }

    private static void ValidateComponent(string name)
    {
        if (name is "" or "." or ".." || name.Any(c => c < ' ' || "<>:\"\\|?*".Contains(c)) ||
            name.EndsWith(' ') || name.EndsWith('.'))
            throw new InvalidDataException("Invalid backup path component.");
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "0123456789¹²³".Contains(stem[3])))
            throw new InvalidDataException("Reserved backup path component.");
    }

    public static string Resolve(string root, string relative, bool allowRoot = false)
    {
        if (Path.IsPathRooted(relative) || relative.Contains('\\'))
            throw new InvalidDataException("Expected a relative DeviceLink path.");
        string path = Path.GetFullPath(root);
        RejectLink(path);
        foreach (string part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            ValidateComponent(part);
            path = Path.Combine(path, part);
            RejectLink(path);
        }
        if (!allowRoot && path == Path.GetFullPath(root))
            throw new InvalidDataException("Expected a file or directory below the backup root.");
        return path;
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links are not supported in backup storage.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    public static bool ContainsPath(string parent, string path)
    {
        string relative = Path.GetRelativePath(parent, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static async Task CopyAsync(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RejectLink(source);
        RejectLink(destination);
        if (Directory.Exists(source))
        {
            if (ContainsPath(source, destination))
                throw new IOException("Cannot copy a directory into itself.");
            CreatePrivateDirectory(destination);
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
                await CopyAsync(entry, Resolve(destination, Path.GetFileName(entry)), token).ConfigureAwait(false);
            Directory.SetLastWriteTimeUtc(destination, Directory.GetLastWriteTimeUtc(source));
        }
        else
        {
            if (Path.GetRelativePath(source, destination) == ".") return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = File.OpenRead(source))
            await using (var output = File.Create(destination))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }
    }

    public static PlistDictionary ReadMetadata(string folder, string file)
    {
        string path = Resolve(folder, file);
        if (new FileInfo(path).Length > 128 * 1024 * 1024)
            throw new InvalidDataException("Backup metadata is too large.");
        return PlistSerializer.Parse(File.ReadAllBytes(path)).AsDictionary();
    }

    public static PlistDictionary ValidateSnapshot(string folder)
    {
        _ = ReadMetadata(folder, "Info.plist");
        var manifest = ReadMetadata(folder, "Manifest.plist");
        if (ReadMetadata(folder, "Status.plist").GetString("SnapshotState") != "finished")
            throw new InvalidDataException("The backup snapshot is incomplete.");
        if (!new[] { "Manifest.db", "Manifest.mbdb" }.Any(name =>
            new FileInfo(Resolve(folder, name)) is { Exists: true, Length: > 0 }))
            throw new InvalidDataException("The backup manifest database is missing or empty.");
        return manifest;
    }

    public void Publish()
    {
        ValidateSnapshot(WorkingSnapshot);
        if (Directory.Exists(Snapshot)) Directory.Move(Snapshot, previous);
        try
        {
            Directory.Move(WorkingSnapshot, Snapshot);
        }
        catch
        {
            if (Directory.Exists(previous)) Directory.Move(previous, Snapshot);
            throw;
        }
    }

    public void Dispose()
    {
        // The next operation also cleans interrupted work. A cleanup error must not mask the protocol result.
        try
        {
            if (Directory.Exists(WorkRoot)) Directory.Delete(WorkRoot, true);
            if (Directory.Exists(previous) && Directory.Exists(Snapshot)) Directory.Delete(previous, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { lease.Dispose(); }
    }
}
