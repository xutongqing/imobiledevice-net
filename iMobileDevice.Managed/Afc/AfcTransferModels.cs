namespace iMobileDevice.Managed.Afc;

/// <summary>
/// A file system entry returned by recursive or non-recursive AFC directory listing.
/// </summary>
public sealed record AfcDirectoryEntry(
    string Path,
    string RelativePath,
    string Name,
    AfcFileInfo Info)
{
    public bool IsDirectory => Info.IsDirectory;
    public bool IsRegularFile => Info.IsRegularFile;
    public bool IsSymbolicLink => Info.IsSymbolicLink;
    public long Size => Info.Size;
}

/// <summary>
/// Options controlling AFC directory enumeration (<c>afc ls</c> / <c>afc ls --recursive</c>).
/// </summary>
public sealed class AfcListOptions
{
    /// <summary>
    /// When <see langword="true"/> and <see cref="MaxDepth"/> is <see langword="null"/>, recursively
    /// traverses all subdirectories. When <see langword="false"/>, lists immediate children only.
    /// </summary>
    public bool Recursive { get; init; }

    /// <summary>
    /// Optional explicit traversal depth. <c>0</c> visits only the root (when <see cref="IncludeRoot"/> is
    /// <see langword="true"/>), <c>1</c> lists immediate children, and <c>-1</c> traverses without limit.
    /// When specified, this takes precedence over <see cref="Recursive"/>.
    /// </summary>
    public int? MaxDepth { get; init; }

    /// <summary>
    /// When <see langword="true"/>, yields the root entry itself before its descendants (matching
    /// pymobiledevice3 <c>dirlist</c>).
    /// </summary>
    public bool IncludeRoot { get; init; }

    /// <summary>
    /// When <see langword="true"/>, skips per-entry or subdirectory read errors while continuing traversal.
    /// </summary>
    public bool IgnoreErrors { get; init; }
}

public enum AfcTransferDirection
{
    Pull,
    Push,
}

public enum AfcTransferStage
{
    DirectoryCreated,
    FileStarted,
    FileProgress,
    FileCompleted,
    EntryFailed,
}

/// <summary>
/// Options controlling AFC downloads (<c>afc pull</c>).
/// </summary>
public sealed class AfcPullOptions
{
    /// <summary>
    /// When <see langword="true"/>, continues downloading remaining entries after per-entry errors and
    /// records failures in <see cref="AfcTransferResult.Failures"/> (equivalent to <c>--ignore-errors</c>).
    /// </summary>
    public bool IgnoreErrors { get; init; }

    /// <summary>
    /// Maximum number of bytes read per AFC <c>FileRefRead</c> chunk. Defaults to 1 MiB.
    /// </summary>
    public int ChunkSize { get; init; } = AfcClient.DefaultTransferChunkSize;

    /// <summary>
    /// When <see langword="true"/>, applies the device's <c>st_mtime</c> to downloaded local files.
    /// </summary>
    public bool PreserveModifyTime { get; init; } = true;
}

/// <summary>
/// Options controlling AFC uploads (<c>afc push</c>).
/// </summary>
public sealed class AfcPushOptions
{
    /// <summary>
    /// Maximum number of bytes written per AFC <c>FileRefWrite</c> chunk. Defaults to 1 MiB.
    /// </summary>
    public int ChunkSize { get; init; } = AfcClient.DefaultTransferChunkSize;
}

/// <summary>
/// Live progress report emitted during <see cref="AfcClient.PullAsync"/> and <see cref="AfcClient.PushAsync"/>.
/// </summary>
public sealed record AfcTransferProgress(
    AfcTransferDirection Direction,
    AfcTransferStage Stage,
    string RemotePath,
    string LocalPath,
    string RelativePath,
    long FileBytesTransferred,
    long? FileTotalBytes,
    long TotalBytesTransferred,
    int CompletedFileCount,
    int CompletedDirectoryCount,
    int FailureCount,
    Exception? Error = null);

/// <summary>
/// Metadata describing a file or directory that completed transfer.
/// </summary>
public sealed record AfcTransferredItem(
    string RemotePath,
    string LocalPath,
    string RelativePath,
    long Size);

/// <summary>
/// Metadata describing an entry that failed during transfer.
/// </summary>
public sealed record AfcTransferFailure(
    string RemotePath,
    string LocalPath,
    string RelativePath,
    Exception Exception)
{
    public AfcErrorCode? ErrorCode => (Exception as AfcException)?.ErrorCode;
}

/// <summary>
/// Summary of an AFC pull or push operation, including completed items and any per-entry failures.
/// </summary>
public sealed record AfcTransferResult(
    AfcTransferDirection Direction,
    string SourcePath,
    string DestinationPath,
    IReadOnlyList<AfcTransferredItem> CompletedFiles,
    IReadOnlyList<AfcTransferredItem> CompletedDirectories,
    IReadOnlyList<AfcTransferFailure> Failures,
    long TotalBytesTransferred)
{
    public bool HasFailures => Failures.Count > 0;
}

/// <summary>
/// Exception thrown when an AFC pull or push fails, preserving the partial result so callers can
/// distinguish completed files from the failed entry.
/// </summary>
public sealed class AfcTransferException : IOException
{
    public AfcTransferException(
        string message,
        AfcTransferResult partialResult,
        AfcTransferFailure failedEntry,
        Exception innerException)
        : base(message, innerException)
    {
        PartialResult = partialResult ?? throw new ArgumentNullException(nameof(partialResult));
        FailedEntry = failedEntry ?? throw new ArgumentNullException(nameof(failedEntry));
    }

    public AfcTransferResult PartialResult { get; }
    public AfcTransferFailure FailedEntry { get; }
    public AfcErrorCode? ErrorCode => (InnerException as AfcException)?.ErrorCode;
}
