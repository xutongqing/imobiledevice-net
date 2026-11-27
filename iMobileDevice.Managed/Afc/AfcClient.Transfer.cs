namespace iMobileDevice.Managed.Afc;

using System.Buffers;
using System.Runtime.CompilerServices;

public sealed partial class AfcClient
{
    private const int MaxTransferChunkSize = 16 * 1024 * 1024; // 16 MiB (well below 64 MiB packet cap)
    private const int MaxSymlinkHops = 16;

    private bool IsClosed => Volatile.Read(ref closed) != 0;

    /// <summary>
    /// Checks whether a path exists on the AFC service.
    /// </summary>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return await TryGetFileInfoAsync(path, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Enumerates entries under <paramref name="remotePath"/> (corresponds to <c>afc ls</c> and <c>afc ls --recursive</c>).
    /// </summary>
    public IAsyncEnumerable<AfcDirectoryEntry> EnumerateDirectoryAsync(
        string remotePath,
        bool recursive = false,
        CancellationToken cancellationToken = default) =>
        EnumerateDirectoryAsync(remotePath, new AfcListOptions { Recursive = recursive }, cancellationToken);

    /// <summary>
    /// Enumerates entries under <paramref name="remotePath"/> with explicit traversal options.
    /// </summary>
    public async IAsyncEnumerable<AfcDirectoryEntry> EnumerateDirectoryAsync(
        string remotePath,
        AfcListOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        options ??= new AfcListOptions();
        if (options.MaxDepth is < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be -1 or greater.");
        }

        string normalizedRoot = NormalizeRemotePath(remotePath);
        int maxDepth = options.MaxDepth ?? (options.Recursive ? -1 : 1);

        ResolvedRemoteEntry resolvedRoot = await ResolveRemoteEntryAsync(normalizedRoot, cancellationToken).ConfigureAwait(false);
        string rootName = GetRemoteFileName(normalizedRoot);

        if (!resolvedRoot.Info.IsDirectory)
        {
            yield return new AfcDirectoryEntry(normalizedRoot, rootName, rootName, resolvedRoot.Info);
            yield break;
        }

        if (options.IncludeRoot)
        {
            yield return new AfcDirectoryEntry(normalizedRoot, string.Empty, rootName, resolvedRoot.Info);
        }

        if (maxDepth == 0)
        {
            yield break;
        }

        var visitedCanonicalDirs = new HashSet<string>(StringComparer.Ordinal)
        {
            CanonicalizeRemotePath(normalizedRoot),
            CanonicalizeRemotePath(resolvedRoot.Path),
        };

        var pending = new Stack<(string DisplayPath, string RemoteReadPath, string RelativePrefix, int Depth)>();
        pending.Push((normalizedRoot, resolvedRoot.Path, string.Empty, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (displayDir, remoteReadDir, relativePrefix, depth) = pending.Pop();

            IReadOnlyList<string> rawNames;
            try
            {
                rawNames = await ReadDirectoryAsync(remoteReadDir, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (CanIgnoreListError(ex, options.IgnoreErrors, cancellationToken))
            {
                continue;
            }

            var subdirsToVisit = new List<(string DisplayPath, string RemoteReadPath, string RelativePrefix, int Depth)>();
            var entryValidator = new DirectoryEntryValidator(rawNames, remoteReadDir);

            foreach (string rawEntry in rawNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool keep;
                try
                {
                    keep = entryValidator.TryAccept(rawEntry);
                }
                catch (Exception ex) when (CanIgnoreListError(ex, options.IgnoreErrors, cancellationToken))
                {
                    continue;
                }

                if (!keep)
                {
                    continue;
                }

                string entryName = rawEntry;
                string childDisplayPath = JoinRemotePath(displayDir, entryName);
                string childReadPath = JoinRemotePath(remoteReadDir, entryName);
                string childRelativePath = string.IsNullOrEmpty(relativePrefix)
                    ? entryName
                    : $"{relativePrefix}/{entryName}";

                AfcFileInfo childInfo;
                try
                {
                    childInfo = await GetFileInfoAsync(childReadPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (CanIgnoreListError(ex, options.IgnoreErrors, cancellationToken))
                {
                    continue;
                }

                yield return new AfcDirectoryEntry(childDisplayPath, childRelativePath, entryName, childInfo);

                // Match upstream walk(): do not descend into symbolic links to directories, and avoid revisiting
                // any canonical directory path so malformed listings cannot form a cycle.
                if (childInfo.IsDirectory && (maxDepth < 0 || depth + 1 < maxDepth))
                {
                    string canonicalChild = CanonicalizeRemotePath(childReadPath);
                    if (visitedCanonicalDirs.Add(canonicalChild))
                    {
                        subdirsToVisit.Add((childDisplayPath, childReadPath, childRelativePath, depth + 1));
                    }
                }
            }

            for (int i = subdirsToVisit.Count - 1; i >= 0; i--)
            {
                pending.Push(subdirsToVisit[i]);
            }
        }
    }

    /// <summary>
    /// Materializes the entries under <paramref name="remotePath"/> (corresponds to <c>afc ls</c> / <c>afc ls --recursive</c>).
    /// </summary>
    public Task<IReadOnlyList<AfcDirectoryEntry>> ListDirectoryAsync(
        string remotePath,
        bool recursive = false,
        CancellationToken cancellationToken = default) =>
        ListDirectoryAsync(remotePath, new AfcListOptions { Recursive = recursive }, cancellationToken);

    /// <summary>
    /// Materializes the entries under <paramref name="remotePath"/> using explicit options.
    /// </summary>
    public async Task<IReadOnlyList<AfcDirectoryEntry>> ListDirectoryAsync(
        string remotePath,
        AfcListOptions? options,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<AfcDirectoryEntry>();
        await foreach (AfcDirectoryEntry entry in EnumerateDirectoryAsync(remotePath, options, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Downloads a remote file or directory tree from the AFC session to the local filesystem
    /// (corresponds to <c>afc pull</c>). Works identically for media AFC and House Arrest app containers.
    /// </summary>
    public async Task<AfcTransferResult> PullAsync(
        string remotePath,
        string localDestinationPath,
        AfcPullOptions? options = null,
        IProgress<AfcTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDestinationPath);
        options ??= new AfcPullOptions();
        ValidateChunkSize(options.ChunkSize, nameof(options));

        string normalizedRemote = NormalizeRemotePath(remotePath);
        string fullLocalDestination = Path.GetFullPath(localDestinationPath);

        var state = new TransferState(AfcTransferDirection.Pull, normalizedRemote, fullLocalDestination, progress);

        ResolvedRemoteEntry rootEntry;
        try
        {
            rootEntry = await ResolveRemoteEntryAsync(normalizedRemote, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
        {
            throw state.CreateFatalException(normalizedRemote, fullLocalDestination, GetRemoteFileName(normalizedRemote), ex);
        }

        if (!rootEntry.Info.IsDirectory)
        {
            string sourceName = GetRemoteFileName(normalizedRemote);
            string targetLocalFile;
            try
            {
                if (Directory.Exists(fullLocalDestination))
                {
                    ValidateLocalPathComponent(sourceName);
                    targetLocalFile = SafeCombineLocalPath(fullLocalDestination, fullLocalDestination, sourceName);
                }
                else
                {
                    EnsureNotReparsePointIfExists(fullLocalDestination);
                    string? parentDir = Path.GetDirectoryName(fullLocalDestination);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    targetLocalFile = fullLocalDestination;
                }
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                throw state.CreateFatalException(normalizedRemote, fullLocalDestination, sourceName, ex);
            }

            state.DestinationPath = targetLocalFile;
            try
            {
                await PullSingleFileAsync(
                    rootEntry.Path,
                    normalizedRemote,
                    targetLocalFile,
                    sourceName,
                    rootEntry.Info,
                    options,
                    state,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                throw state.CreateFatalException(normalizedRemote, targetLocalFile, sourceName, ex);
            }

            return state.BuildResult();
        }

        // Source is a directory: align with upstream pull(), placing it under dst/<basename(src)>
        // (or directly into dst when pulling the root "/").
        string sourceDirName = GetRemoteFileName(normalizedRemote);
        string localTreeRoot;
        try
        {
            if (File.Exists(fullLocalDestination))
            {
                throw new IOException($"Local destination '{fullLocalDestination}' already exists as a file.");
            }

            if (string.IsNullOrEmpty(sourceDirName))
            {
                EnsureNotReparsePointIfExists(fullLocalDestination);
                localTreeRoot = fullLocalDestination;
            }
            else
            {
                ValidateLocalPathComponent(sourceDirName);
                localTreeRoot = SafeCombineLocalPath(fullLocalDestination, fullLocalDestination, sourceDirName);
            }

            EnsureLocalDirectorySafe(fullLocalDestination, localTreeRoot);
        }
        catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
        {
            throw state.CreateFatalException(normalizedRemote, fullLocalDestination, sourceDirName, ex);
        }

        state.DestinationPath = localTreeRoot;
        state.RecordDirectoryCompleted(normalizedRemote, localTreeRoot, string.Empty);

        var activeAncestors = new HashSet<string>(StringComparer.Ordinal)
        {
            CanonicalizeRemotePath(normalizedRemote),
            CanonicalizeRemotePath(rootEntry.Path),
        };

        await PullDirectoryRecursiveAsync(
            rootEntry.Path,
            normalizedRemote,
            localTreeRoot,
            localTreeRoot,
            string.Empty,
            activeAncestors,
            options,
            state,
            cancellationToken).ConfigureAwait(false);

        return state.BuildResult();
    }

    /// <summary>
    /// Uploads a local file or directory tree to the AFC session (corresponds to <c>afc push</c>).
    /// Works identically for media AFC and House Arrest app containers.
    /// </summary>
    public async Task<AfcTransferResult> PushAsync(
        string localSourcePath,
        string remoteDestinationPath,
        AfcPushOptions? options = null,
        IProgress<AfcTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteDestinationPath);
        options ??= new AfcPushOptions();
        ValidateChunkSize(options.ChunkSize, nameof(options));

        string fullLocalSource = Path.GetFullPath(localSourcePath);
        string normalizedRemoteDest = NormalizeRemotePath(remoteDestinationPath);

        bool isLocalDir = Directory.Exists(fullLocalSource);
        bool isLocalFile = !isLocalDir && File.Exists(fullLocalSource);
        if (!isLocalDir && !isLocalFile)
        {
            throw new FileNotFoundException($"Local source path does not exist: '{fullLocalSource}'.", fullLocalSource);
        }

        var state = new TransferState(AfcTransferDirection.Push, fullLocalSource, normalizedRemoteDest, progress);

        if (isLocalFile)
        {
            string localFileName = Path.GetFileName(fullLocalSource);
            string targetRemoteFile;
            try
            {
                targetRemoteFile = await ResolvePushFileDestinationAsync(normalizedRemoteDest, localFileName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                throw state.CreateFatalException(normalizedRemoteDest, fullLocalSource, localFileName, ex);
            }

            state.DestinationPath = targetRemoteFile;
            try
            {
                await PushSingleFileAsync(fullLocalSource, targetRemoteFile, localFileName, options, state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                throw state.CreateFatalException(targetRemoteFile, fullLocalSource, localFileName, ex);
            }

            return state.BuildResult();
        }

        // Local source is a directory: align with upstream push(), copying into remote_path/<basename(local_path)>.
        string trimmedLocalSource = Path.TrimEndingDirectorySeparator(fullLocalSource);
        string sourceDirName = Path.GetFileName(trimmedLocalSource);
        string remoteTreeRoot = string.IsNullOrEmpty(sourceDirName)
            ? normalizedRemoteDest
            : JoinRemotePath(normalizedRemoteDest, sourceDirName);

        state.DestinationPath = remoteTreeRoot;

        try
        {
            AfcFileInfo? destInfo = await TryGetFileInfoAsync(normalizedRemoteDest, cancellationToken).ConfigureAwait(false);
            if (destInfo is not null)
            {
                if (destInfo.IsSymbolicLink)
                {
                    destInfo = (await ResolveRemoteEntryAsync(normalizedRemoteDest, cancellationToken).ConfigureAwait(false)).Info;
                }

                if (!destInfo.IsDirectory)
                {
                    throw new AfcException(
                        $"Remote destination '{normalizedRemoteDest}' exists and is not a directory.",
                        AfcErrorCode.ObjectExists);
                }
            }

            await EnsureRemoteDirectoryAsync(remoteTreeRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
        {
            throw state.CreateFatalException(remoteTreeRoot, fullLocalSource, string.Empty, ex);
        }

        state.RecordDirectoryCompleted(remoteTreeRoot, fullLocalSource, string.Empty);

        var activeLocalAncestors = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            ResolveLocalRealPath(trimmedLocalSource),
        };

        await PushDirectoryRecursiveAsync(
            trimmedLocalSource,
            remoteTreeRoot,
            string.Empty,
            activeLocalAncestors,
            options,
            state,
            cancellationToken).ConfigureAwait(false);

        return state.BuildResult();
    }

    private async Task PullDirectoryRecursiveAsync(
        string remoteReadDir,
        string remoteDisplayDir,
        string localTreeRoot,
        string currentLocalDir,
        string relativePrefix,
        HashSet<string> activeAncestors,
        AfcPullOptions options,
        TransferState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> rawEntries;
        try
        {
            rawEntries = await ReadDirectoryAsync(remoteReadDir, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
        {
            HandlePullEntryError(remoteDisplayDir, currentLocalDir, relativePrefix, ex, options, state, cancellationToken);
            return;
        }

        var entryValidator = new DirectoryEntryValidator(rawEntries, remoteDisplayDir);

        foreach (string rawEntry in rawEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string childRelative = string.IsNullOrEmpty(relativePrefix)
                ? rawEntry
                : $"{relativePrefix}/{rawEntry}";
            string childRemoteDisplay = JoinRemotePath(remoteDisplayDir, rawEntry);

            bool keep;
            try
            {
                keep = entryValidator.TryAccept(rawEntry);
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                HandlePullEntryError(childRemoteDisplay, currentLocalDir, childRelative, ex, options, state, cancellationToken);
                continue;
            }

            if (!keep)
            {
                continue;
            }

            string entryName = rawEntry;
            string childRemoteRead = JoinRemotePath(remoteReadDir, entryName);

            string childLocalPath;
            try
            {
                ValidateLocalPathComponent(entryName);
                childLocalPath = SafeCombineLocalPath(localTreeRoot, currentLocalDir, entryName);
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                HandlePullEntryError(childRemoteDisplay, currentLocalDir, childRelative, ex, options, state, cancellationToken);
                continue;
            }

            try
            {
                ResolvedRemoteEntry resolved = await ResolveRemoteEntryAsync(childRemoteRead, cancellationToken).ConfigureAwait(false);
                if (resolved.Info.IsDirectory)
                {
                    string canonicalTarget = CanonicalizeRemotePath(resolved.Path);
                    if (!activeAncestors.Add(canonicalTarget))
                    {
                        throw new InvalidDataException(
                            $"Directory traversal cycle detected at '{childRemoteDisplay}' (target '{resolved.Path}').");
                    }

                    try
                    {
                        EnsureLocalDirectorySafe(localTreeRoot, childLocalPath);
                        state.RecordDirectoryCompleted(childRemoteDisplay, childLocalPath, childRelative);

                        await PullDirectoryRecursiveAsync(
                            resolved.Path,
                            childRemoteDisplay,
                            localTreeRoot,
                            childLocalPath,
                            childRelative,
                            activeAncestors,
                            options,
                            state,
                            cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        activeAncestors.Remove(canonicalTarget);
                    }
                }
                else if (resolved.Info.IsRegularFile)
                {
                    await PullSingleFileAsync(
                        resolved.Path,
                        childRemoteDisplay,
                        childLocalPath,
                        childRelative,
                        resolved.Info,
                        options,
                        state,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw new AfcException(
                        $"Unsupported AFC file type '{resolved.Info.FileType}' at '{childRemoteDisplay}'.",
                        AfcErrorCode.InvalidArg);
                }
            }
            catch (AfcTransferException)
            {
                throw;
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                HandlePullEntryError(childRemoteDisplay, childLocalPath, childRelative, ex, options, state, cancellationToken);
            }
        }
    }

    private async Task PullSingleFileAsync(
        string remoteReadPath,
        string remoteDisplayPath,
        string localTargetPath,
        string relativePath,
        AfcFileInfo fileInfo,
        AfcPullOptions options,
        TransferState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(localTargetPath))
        {
            throw new IOException($"Cannot overwrite existing local directory '{localTargetPath}' with file '{remoteDisplayPath}'.");
        }

        EnsureNotReparsePointIfExists(localTargetPath);
        string parentDir = Path.GetDirectoryName(localTargetPath)
            ?? throw new IOException($"Cannot determine parent directory for '{localTargetPath}'.");
        Directory.CreateDirectory(parentDir);

        string tempFilePath = Path.Combine(parentDir, $".{Path.GetFileName(localTargetPath)}.{Guid.NewGuid():N}.afctmp");
        long expectedSize = fileInfo.Size >= 0 ? fileInfo.Size : 0;
        long fileBytes = 0;

        state.Report(AfcTransferStage.FileStarted, remoteDisplayPath, localTargetPath, relativePath, 0, expectedSize);

        ulong handle = await OpenFileHandleAsync(remoteReadPath, AfcFileOpenMode.ReadOnly, cancellationToken).ConfigureAwait(false);
        bool readSucceeded = false;
        bool movedIntoPlace = false;
        try
        {
            await using (var output = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: Math.Min(options.ChunkSize, 128 * 1024),
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] chunk = await ReadFileChunkAsync(handle, options.ChunkSize, cancellationToken).ConfigureAwait(false);
                    if (chunk.Length == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                    fileBytes += chunk.Length;
                    state.AddTransferredBytes(chunk.Length);
                    state.Report(AfcTransferStage.FileProgress, remoteDisplayPath, localTargetPath, relativePath, fileBytes, expectedSize);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            readSucceeded = true;
        }
        finally
        {
            await CloseHandleBestEffortAsync(handle, throwOnError: readSucceeded).ConfigureAwait(false);
            if (!readSucceeded)
            {
                TryDeleteFile(tempFilePath);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.PreserveModifyTime && fileInfo.ModifyTime is DateTimeOffset mtime)
            {
                File.SetLastWriteTimeUtc(tempFilePath, mtime.UtcDateTime);
            }

            EnsureNotReparsePointIfExists(localTargetPath);
            File.Move(tempFilePath, localTargetPath, overwrite: true);
            movedIntoPlace = true;
        }
        finally
        {
            if (!movedIntoPlace)
            {
                TryDeleteFile(tempFilePath);
            }
        }

        state.RecordFileCompleted(remoteDisplayPath, localTargetPath, relativePath, fileBytes);
    }

    private async Task PushDirectoryRecursiveAsync(
        string currentLocalDir,
        string currentRemoteDir,
        string relativePrefix,
        HashSet<string> activeLocalAncestors,
        AfcPushOptions options,
        TransferState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string[] localEntries;
        try
        {
            localEntries = Directory.GetFileSystemEntries(currentLocalDir);
            Array.Sort(localEntries, StringComparer.Ordinal);
        }
        catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
        {
            throw state.CreateFatalException(currentRemoteDir, currentLocalDir, relativePrefix, ex);
        }

        foreach (string childLocalPath in localEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(childLocalPath);
            string childRemotePath = JoinRemotePath(currentRemoteDir, name);
            string childRelative = string.IsNullOrEmpty(relativePrefix)
                ? name
                : $"{relativePrefix}/{name}";

            try
            {
                if (Directory.Exists(childLocalPath))
                {
                    string realLocalDir = ResolveLocalRealPath(childLocalPath);
                    if (!activeLocalAncestors.Add(realLocalDir))
                    {
                        throw new InvalidDataException($"Local directory symlink cycle detected at '{childLocalPath}'.");
                    }

                    try
                    {
                        await EnsureRemoteDirectoryAsync(childRemotePath, cancellationToken).ConfigureAwait(false);
                        state.RecordDirectoryCompleted(childRemotePath, childLocalPath, childRelative);

                        await PushDirectoryRecursiveAsync(
                            childLocalPath,
                            childRemotePath,
                            childRelative,
                            activeLocalAncestors,
                            options,
                            state,
                            cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        activeLocalAncestors.Remove(realLocalDir);
                    }
                }
                else
                {
                    await PushSingleFileAsync(childLocalPath, childRemotePath, childRelative, options, state, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (AfcTransferException)
            {
                throw;
            }
            catch (Exception ex) when (IsNonCancellationError(ex, cancellationToken))
            {
                throw state.CreateFatalException(childRemotePath, childLocalPath, childRelative, ex);
            }
        }
    }

    private async Task PushSingleFileAsync(
        string localFilePath,
        string remoteFilePath,
        string relativePath,
        AfcPushOptions options,
        TransferState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localInfo = new FileInfo(localFilePath);
        long expectedSize = localInfo.Length;
        long fileBytes = 0;

        state.Report(AfcTransferStage.FileStarted, remoteFilePath, localFilePath, relativePath, 0, expectedSize);

        await using var input = new FileStream(
            localFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: Math.Min(options.ChunkSize, 128 * 1024),
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        ulong handle = await OpenFileHandleAsync(remoteFilePath, AfcFileOpenMode.WriteOnly, cancellationToken).ConfigureAwait(false);
        bool writeSucceeded = false;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(options.ChunkSize);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesRead = await input.ReadAsync(buffer.AsMemory(0, options.ChunkSize), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await WriteFileChunkAsync(handle, buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                fileBytes += bytesRead;
                state.AddTransferredBytes(bytesRead);
                state.Report(AfcTransferStage.FileProgress, remoteFilePath, localFilePath, relativePath, fileBytes, expectedSize);
            }

            writeSucceeded = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await CloseHandleBestEffortAsync(handle, throwOnError: writeSucceeded).ConfigureAwait(false);
        }

        state.RecordFileCompleted(remoteFilePath, localFilePath, relativePath, fileBytes);
    }

    private async Task<string> ResolvePushFileDestinationAsync(
        string normalizedRemoteDest,
        string localFileName,
        CancellationToken cancellationToken)
    {
        AfcFileInfo? destInfo = await TryGetFileInfoAsync(normalizedRemoteDest, cancellationToken).ConfigureAwait(false);
        if (destInfo is not null)
        {
            if (destInfo.IsSymbolicLink)
            {
                ResolvedRemoteEntry resolved = await ResolveRemoteEntryAsync(normalizedRemoteDest, cancellationToken).ConfigureAwait(false);
                destInfo = resolved.Info;
            }

            if (destInfo.IsDirectory)
            {
                string childPath = JoinRemotePath(normalizedRemoteDest, localFileName);
                AfcFileInfo? existingChild = await TryGetFileInfoAsync(childPath, cancellationToken).ConfigureAwait(false);
                if (existingChild is not null && existingChild.IsDirectory)
                {
                    throw new AfcException(
                        $"Cannot overwrite remote directory '{childPath}' with file '{localFileName}'.",
                        AfcErrorCode.ObjectIsDir);
                }

                return childPath;
            }

            return normalizedRemoteDest;
        }

        string remoteParent = GetRemoteParentPath(normalizedRemoteDest);
        if (!string.IsNullOrEmpty(remoteParent))
        {
            AfcFileInfo? parentInfo = await TryGetFileInfoAsync(remoteParent, cancellationToken).ConfigureAwait(false);
            if (parentInfo is null)
            {
                throw new AfcException(
                    $"Remote parent directory '{remoteParent}' does not exist.",
                    AfcErrorCode.ObjectNotFound);
            }

            if (parentInfo.IsSymbolicLink)
            {
                parentInfo = (await ResolveRemoteEntryAsync(remoteParent, cancellationToken).ConfigureAwait(false)).Info;
            }

            if (!parentInfo.IsDirectory)
            {
                throw new AfcException(
                    $"Remote parent path '{remoteParent}' is not a directory.",
                    AfcErrorCode.InvalidArg);
            }
        }

        return normalizedRemoteDest;
    }

    private async Task EnsureRemoteDirectoryAsync(string remoteDir, CancellationToken cancellationToken)
    {
        string normalized = NormalizeRemotePath(remoteDir);
        if (normalized == "/")
        {
            return;
        }

        AfcFileInfo? existing = await TryGetFileInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.IsSymbolicLink)
            {
                existing = (await ResolveRemoteEntryAsync(normalized, cancellationToken).ConfigureAwait(false)).Info;
            }

            if (existing.IsDirectory)
            {
                return;
            }

            throw new AfcException(
                $"Remote path '{normalized}' exists and is not a directory.",
                AfcErrorCode.ObjectExists);
        }

        string parent = GetRemoteParentPath(normalized);
        if (!string.IsNullOrEmpty(parent) && parent != normalized)
        {
            await EnsureRemoteDirectoryAsync(parent, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await MakeDirectoryAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (AfcException ex) when (ex.ErrorCode == AfcErrorCode.ObjectExists)
        {
            AfcFileInfo info = await GetFileInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
            if (!info.IsDirectory)
            {
                throw;
            }
        }
    }

    private async Task<AfcFileInfo?> TryGetFileInfoAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetFileInfoAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (AfcException ex) when (ex.ErrorCode is AfcErrorCode.ObjectNotFound or AfcErrorCode.ReadError)
        {
            return null;
        }
    }

    private async Task<ResolvedRemoteEntry> ResolveRemoteEntryAsync(string remotePath, CancellationToken cancellationToken)
    {
        string currentPath = NormalizeRemotePath(remotePath);
        var seenLinks = new HashSet<string>(StringComparer.Ordinal);

        for (int hop = 0; hop <= MaxSymlinkHops; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AfcFileInfo info = await GetFileInfoAsync(currentPath, cancellationToken).ConfigureAwait(false);
            if (!info.IsSymbolicLink)
            {
                return new ResolvedRemoteEntry(currentPath, info);
            }

            string canonicalCurrent = CanonicalizeRemotePath(currentPath);
            if (!seenLinks.Add(canonicalCurrent))
            {
                throw new InvalidDataException($"Symbolic link cycle detected at '{remotePath}'.");
            }

            if (hop == MaxSymlinkHops)
            {
                throw new InvalidDataException($"Too many symbolic link hops while resolving '{remotePath}'.");
            }

            string? linkTarget = info.LinkTarget;
            if (string.IsNullOrEmpty(linkTarget) || linkTarget.Contains('\0'))
            {
                throw new InvalidDataException($"Symbolic link '{currentPath}' has an invalid LinkTarget.");
            }

            string resolvedTarget = linkTarget.StartsWith('/')
                ? CanonicalizeRemotePath(linkTarget)
                : CanonicalizeRemotePath(JoinRemotePath(GetRemoteParentPath(currentPath), linkTarget));

            currentPath = resolvedTarget;
        }

        throw new InvalidDataException($"Too many symbolic link hops while resolving '{remotePath}'.");
    }

    private async Task CloseHandleBestEffortAsync(ulong handle, bool throwOnError)
    {
        if (IsClosed)
        {
            return;
        }

        if (throwOnError)
        {
            await CloseFileHandleAsync(handle, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            await CloseFileHandleAsync(handle, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AfcException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Keep the primary failure or cancellation as the thrown exception.
        }
    }

    private void HandlePullEntryError(
        string remotePath,
        string localPath,
        string relativePath,
        Exception ex,
        AfcPullOptions options,
        TransferState state,
        CancellationToken cancellationToken)
    {
        if (IsFatalTransportFailure(ex, cancellationToken))
        {
            throw state.CreateFatalException(remotePath, localPath, relativePath, ex);
        }

        AfcTransferFailure failure = state.RecordFailure(remotePath, localPath, relativePath, ex);
        if (!options.IgnoreErrors)
        {
            throw state.CreateFatalException(failure);
        }
    }

    private bool CanIgnoreListError(Exception ex, bool ignoreErrors, CancellationToken cancellationToken) =>
        ignoreErrors &&
        IsNonCancellationError(ex, cancellationToken) &&
        !IsFatalTransportFailure(ex, cancellationToken);

    private bool IsFatalTransportFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (IsClosed || ex is ObjectDisposedException)
        {
            return true;
        }

        if (ex is AfcException afcEx)
        {
            return afcEx.ErrorCode is AfcErrorCode.ServiceNotConnected
                or AfcErrorCode.OpHeaderInvalid
                or AfcErrorCode.UnknownPacketType;
        }

        return false;
    }

    private static bool IsNonCancellationError(Exception ex, CancellationToken cancellationToken) =>
        !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested);

    private static void ValidateChunkSize(int chunkSize, string paramName)
    {
        if (chunkSize <= 0 || chunkSize > MaxTransferChunkSize)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                $"ChunkSize must be between 1 and {MaxTransferChunkSize} bytes.");
        }
    }

    private struct DirectoryEntryValidator
    {
        private readonly string remoteDir;
        private readonly bool hasDotAnywhere;
        private bool sawDot;
        private bool sawDotDot;

        public DirectoryEntryValidator(IReadOnlyList<string> rawEntries, string remoteDir)
        {
            this.remoteDir = remoteDir;
            hasDotAnywhere = false;
            for (int i = 0; i < rawEntries.Count; i++)
            {
                if (rawEntries[i] == ".")
                {
                    hasDotAnywhere = true;
                    break;
                }
            }
        }

        public bool TryAccept(string entry)
        {
            if (string.IsNullOrEmpty(entry))
            {
                return false;
            }

            if (entry == ".")
            {
                if (sawDot)
                {
                    throw new InvalidDataException($"Duplicate '.' directory entry returned by device under '{remoteDir}'.");
                }

                sawDot = true;
                return false;
            }

            if (entry == "..")
            {
                // Standard POSIX/AFC listings return '.' and '..' together; a lone '..' or a duplicate '..'
                // indicates a malicious or malformed traversal entry.
                if (!hasDotAnywhere || sawDotDot)
                {
                    throw new InvalidDataException($"Unsafe '..' directory entry returned by device under '{remoteDir}'.");
                }

                sawDotDot = true;
                return false;
            }

            if (entry.Contains('/') || entry.Contains('\\') || entry.Contains('\0'))
            {
                throw new InvalidDataException($"Invalid directory entry name '{entry}' returned by device under '{remoteDir}'.");
            }

            return true;
        }
    }

    internal static string NormalizeRemotePath(string path)
    {
        if (path.Contains('\0'))
        {
            throw new ArgumentException("Remote path must not contain NUL characters.", nameof(path));
        }

        string replaced = path.Replace('\\', '/');
        bool rooted = replaced.StartsWith('/');
        string[] parts = replaced.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return rooted ? "/" : ".";
        }

        string joined = string.Join('/', parts);
        return rooted ? "/" + joined : joined;
    }

    internal static string CanonicalizeRemotePath(string path)
    {
        string replaced = path.Replace('\\', '/');
        var stack = new List<string>();
        foreach (string part in replaced.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return "/" + string.Join('/', stack);
    }

    internal static string JoinRemotePath(string parent, string child)
    {
        if (string.IsNullOrEmpty(parent) || parent == ".")
        {
            return child;
        }

        if (parent == "/")
        {
            return "/" + child;
        }

        return parent.EndsWith('/') ? parent + child : parent + "/" + child;
    }

    internal static string GetRemoteFileName(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath) || normalizedPath is "/" or ".")
        {
            return string.Empty;
        }

        int slash = normalizedPath.LastIndexOf('/');
        return slash < 0 ? normalizedPath : normalizedPath[(slash + 1)..];
    }

    internal static string GetRemoteParentPath(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath) || normalizedPath is "/" or ".")
        {
            return "/";
        }

        int slash = normalizedPath.LastIndexOf('/');
        if (slash < 0)
        {
            return "/";
        }

        return slash == 0 ? "/" : normalizedPath[..slash];
    }

    internal static void ValidateLocalPathComponent(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or "..")
        {
            throw new InvalidDataException($"Invalid device file name component: '{name}'.");
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains('\0'))
        {
            throw new InvalidDataException($"Device file name contains path separator or NUL: '{name}'.");
        }

        // Reject Windows drive-relative prefixes such as "C:" or "C:foo" on all hosts.
        if (name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':')
        {
            throw new InvalidDataException($"Device file name contains a drive prefix: '{name}'.");
        }

        // Reject reserved Windows DOS device names (CON, NUL, COM1, LPT1, etc.) on all hosts,
        // matching upstream pymobiledevice3/safe_paths.py.
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (IsWindowsReservedDeviceName(stem))
        {
            throw new InvalidDataException($"Device file name uses a reserved Windows device name: '{name}'.");
        }

        if (OperatingSystem.IsWindows())
        {
            if (name.EndsWith(' ') || name.EndsWith('.') || name.Any(c => c < ' ' || "<>:\"|?*".Contains(c)))
            {
                throw new InvalidDataException($"Device file name is not valid on Windows: '{name}'.");
            }
        }
    }

    private static bool IsWindowsReservedDeviceName(string upperStem) =>
        upperStem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
        (upperStem.Length == 4 &&
         (upperStem.StartsWith("COM", StringComparison.Ordinal) || upperStem.StartsWith("LPT", StringComparison.Ordinal)) &&
         "123456789\u00b9\u00b2\u00b3".Contains(upperStem[3]));

    private static string SafeCombineLocalPath(string boundaryRoot, string currentDir, string component)
    {
        ValidateLocalPathComponent(component);
        string fullRoot = Path.GetFullPath(boundaryRoot);
        string candidate = Path.GetFullPath(Path.Combine(currentDir, component));

        if (!IsPathStrictlyInside(fullRoot, candidate))
        {
            throw new InvalidDataException(
                $"Device entry '{component}' resolves to '{candidate}', which escapes destination root '{fullRoot}'.");
        }

        EnsureNotReparsePointIfExists(candidate);
        return candidate;
    }

    private static void EnsureLocalDirectorySafe(string boundaryRoot, string directoryPath)
    {
        string fullBoundary = Path.GetFullPath(boundaryRoot);
        string fullTarget = Path.GetFullPath(directoryPath);
        if (!string.Equals(fullBoundary, fullTarget, PathComparison) && !IsPathStrictlyInside(fullBoundary, fullTarget))
        {
            throw new InvalidDataException($"Target directory '{fullTarget}' escapes boundary '{fullBoundary}'.");
        }

        if (File.Exists(fullTarget))
        {
            throw new IOException($"Cannot create local directory '{fullTarget}' because a file already exists at that path.");
        }

        EnsureNotReparsePointIfExists(fullTarget);
        Directory.CreateDirectory(fullTarget);
        EnsureNotReparsePointIfExists(fullTarget);
    }

    private static void EnsureNotReparsePointIfExists(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Refusing to traverse or overwrite local symbolic link/reparse point '{path}'.");
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static bool IsPathStrictlyInside(string rootPath, string candidatePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        if (string.Equals(normalizedRoot, normalizedCandidate, PathComparison))
        {
            return false;
        }

        string rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootWithSeparator, PathComparison);
    }

    private static string ResolveLocalRealPath(string path)
    {
        try
        {
            FileSystemInfo? target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target is not null)
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly record struct ResolvedRemoteEntry(string Path, AfcFileInfo Info);

    private sealed class TransferState
    {
        private readonly AfcTransferDirection direction;
        private readonly string sourcePath;
        private readonly IProgress<AfcTransferProgress>? progress;
        private readonly List<AfcTransferredItem> completedFiles = [];
        private readonly List<AfcTransferredItem> completedDirectories = [];
        private readonly List<AfcTransferFailure> failures = [];
        private long totalBytesTransferred;

        public TransferState(
            AfcTransferDirection direction,
            string sourcePath,
            string destinationPath,
            IProgress<AfcTransferProgress>? progress)
        {
            this.direction = direction;
            this.sourcePath = sourcePath;
            DestinationPath = destinationPath;
            this.progress = progress;
        }

        public string DestinationPath { get; set; }

        public void AddTransferredBytes(long bytes) => totalBytesTransferred += bytes;

        public void RecordDirectoryCompleted(string remotePath, string localPath, string relativePath)
        {
            completedDirectories.Add(new AfcTransferredItem(remotePath, localPath, relativePath, 0));
            Report(AfcTransferStage.DirectoryCreated, remotePath, localPath, relativePath, 0, null);
        }

        public void RecordFileCompleted(string remotePath, string localPath, string relativePath, long size)
        {
            completedFiles.Add(new AfcTransferredItem(remotePath, localPath, relativePath, size));
            Report(AfcTransferStage.FileCompleted, remotePath, localPath, relativePath, size, size);
        }

        public AfcTransferFailure RecordFailure(string remotePath, string localPath, string relativePath, Exception error)
        {
            var failure = new AfcTransferFailure(remotePath, localPath, relativePath, error);
            failures.Add(failure);
            Report(AfcTransferStage.EntryFailed, remotePath, localPath, relativePath, 0, null, error);
            return failure;
        }

        public void Report(
            AfcTransferStage stage,
            string remotePath,
            string localPath,
            string relativePath,
            long fileBytesTransferred,
            long? fileTotalBytes,
            Exception? error = null)
        {
            progress?.Report(new AfcTransferProgress(
                direction,
                stage,
                remotePath,
                localPath,
                relativePath,
                fileBytesTransferred,
                fileTotalBytes,
                totalBytesTransferred,
                completedFiles.Count,
                completedDirectories.Count,
                failures.Count,
                error));
        }

        public AfcTransferResult BuildResult() =>
            new(
                direction,
                sourcePath,
                DestinationPath,
                completedFiles.ToArray(),
                completedDirectories.ToArray(),
                failures.ToArray(),
                totalBytesTransferred);

        public AfcTransferException CreateFatalException(string remotePath, string localPath, string relativePath, Exception error)
        {
            AfcTransferFailure failure = RecordFailure(remotePath, localPath, relativePath, error);
            return CreateFatalException(failure);
        }

        public AfcTransferException CreateFatalException(AfcTransferFailure failure)
        {
            AfcTransferResult partial = BuildResult();
            string message =
                $"AFC {direction} failed at '{failure.RemotePath}' (completed {partial.CompletedFiles.Count} file(s)): {failure.Exception.Message}";
            return new AfcTransferException(message, partial, failure, failure.Exception);
        }
    }
}
