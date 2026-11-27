namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using iMobileDevice.Managed.Afc;
using Xunit;

public sealed class AfcTransferTests : IDisposable
{
    private readonly string tempRoot;

    public AfcTransferTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), $"imobile-afc-transfer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task RoundTripDirectoryTree_PushListPull_VerifiesPathsEmptyDirsLengthsAndSha256()
    {
        string sourceRoot = Path.Combine(tempRoot, "src", "sample_tree");
        string downloadParent = Path.Combine(tempRoot, "downloaded");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(downloadParent);

        var expectedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["readme.txt"] = Encoding.UTF8.GetBytes("hello afc transfer\nline 2\n"),
            ["empty.bin"] = [],
            ["nested/sub/note.txt"] = Encoding.UTF8.GetBytes("nested note content"),
            ["nested/deep/large.bin"] = CreateDeterministicBytes(AfcClient.DefaultTransferChunkSize + 131_077, seed: 42),
        };
        string[] expectedEmptyDirs = ["empty_top", "nested/empty_nested", "nested/deep/empty_leaf"];

        foreach ((string rel, byte[] content) in expectedFiles)
        {
            string full = Path.Combine(sourceRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, content);
        }

        foreach (string emptyDir in expectedEmptyDirs)
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, emptyDir.Replace('/', Path.DirectorySeparatorChar)));
        }

        await using var server = new SimulatedAfcServer();
        await using var afc = server.CreateClient();

        // 1. Push directory tree into /Documents (same interface works for media AFC and House Arrest containers).
        await afc.MakeDirectoryAsync("/Documents");
        var pushEvents = new List<AfcTransferProgress>();
        AfcTransferResult pushResult = await afc.PushAsync(
            sourceRoot,
            "/Documents",
            progress: new SyncProgress<AfcTransferProgress>(pushEvents.Add));

        Assert.Equal(AfcTransferDirection.Push, pushResult.Direction);
        Assert.Equal("/Documents/sample_tree", pushResult.DestinationPath);
        Assert.False(pushResult.HasFailures);
        Assert.Equal(expectedFiles.Count, pushResult.CompletedFiles.Count);
        Assert.Equal(expectedFiles.Values.Sum(v => (long)v.Length), pushResult.TotalBytesTransferred);
        Assert.True(server.WriteChunkCount >= 2, "Expected large file push to span multiple AFC write chunks.");
        Assert.Contains(pushEvents, e => e.Stage == AfcTransferStage.FileProgress && e.RelativePath == "nested/deep/large.bin");
        Assert.Contains(pushEvents, e => e.Stage == AfcTransferStage.FileCompleted && e.RelativePath == "empty.bin" && e.FileBytesTransferred == 0);

        // 2. List non-recursively (depth 1) and recursively (afc ls / afc ls --recursive).
        IReadOnlyList<AfcDirectoryEntry> topLevel = await afc.ListDirectoryAsync("/Documents/sample_tree", recursive: false);
        Assert.Equal(
            ["empty.bin", "empty_top", "nested", "readme.txt"],
            topLevel.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());

        IReadOnlyList<AfcDirectoryEntry> recursive = await afc.ListDirectoryAsync("/Documents/sample_tree", recursive: true);
        var listedByRel = recursive.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        foreach ((string rel, byte[] content) in expectedFiles)
        {
            Assert.True(listedByRel.TryGetValue(rel, out AfcDirectoryEntry? entry), $"Missing listed file '{rel}'.");
            Assert.True(entry!.IsRegularFile);
            Assert.Equal(content.Length, entry.Size);
            Assert.Equal($"/Documents/sample_tree/{rel}", entry.Path);
        }

        foreach (string dir in new[] { "empty_top", "nested", "nested/sub", "nested/deep", "nested/empty_nested", "nested/deep/empty_leaf" })
        {
            Assert.True(listedByRel.TryGetValue(dir, out AfcDirectoryEntry? entry), $"Missing listed directory '{dir}'.");
            Assert.True(entry!.IsDirectory);
        }

        // Also verify IncludeRoot aligns with pymobiledevice3 dirlist.
        IReadOnlyList<AfcDirectoryEntry> withRoot = await afc.ListDirectoryAsync(
            "/Documents/sample_tree",
            new AfcListOptions { Recursive = true, IncludeRoot = true });
        Assert.Equal("/Documents/sample_tree", withRoot[0].Path);
        Assert.Equal(string.Empty, withRoot[0].RelativePath);
        Assert.True(withRoot[0].IsDirectory);

        // 3. Pull directory tree back to local filesystem.
        var pullEvents = new List<AfcTransferProgress>();
        AfcTransferResult pullResult = await afc.PullAsync(
            "/Documents/sample_tree",
            downloadParent,
            progress: new SyncProgress<AfcTransferProgress>(pullEvents.Add));

        string pulledRoot = Path.Combine(downloadParent, "sample_tree");
        Assert.Equal(AfcTransferDirection.Pull, pullResult.Direction);
        Assert.Equal(pulledRoot, pullResult.DestinationPath);
        Assert.False(pullResult.HasFailures);
        Assert.Equal(expectedFiles.Count, pullResult.CompletedFiles.Count);
        Assert.Equal(expectedFiles.Values.Sum(v => (long)v.Length), pullResult.TotalBytesTransferred);
        Assert.True(server.ReadChunkCount >= 2, "Expected large file pull to span multiple AFC read chunks.");
        Assert.Contains(pullEvents, e => e.Stage == AfcTransferStage.FileProgress && e.RelativePath == "nested/deep/large.bin");

        // Verify relative paths, empty dirs, lengths, and SHA-256 digests.
        string[] actualRelFiles = Directory
            .EnumerateFiles(pulledRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(pulledRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedFiles.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray(), actualRelFiles);

        foreach ((string rel, byte[] expectedBytes) in expectedFiles)
        {
            string pulledFile = Path.Combine(pulledRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            byte[] actualBytes = await File.ReadAllBytesAsync(pulledFile);
            Assert.Equal(expectedBytes.Length, actualBytes.Length);
            Assert.Equal(SHA256.HashData(expectedBytes), SHA256.HashData(actualBytes));
        }

        foreach (string emptyDir in expectedEmptyDirs)
        {
            string pulledEmpty = Path.Combine(pulledRoot, emptyDir.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(pulledEmpty), $"Expected empty directory '{emptyDir}' to exist locally.");
            Assert.Empty(Directory.EnumerateFileSystemEntries(pulledEmpty));
        }
    }

    [Fact]
    public async Task SingleFileAndDirectory_DestinationIsExistingDirectoryAndOverwriteBehavior()
    {
        await using var server = new SimulatedAfcServer();
        server.AddDirectory("/dest_dir");
        server.AddFile("/dest_dir/existing.txt", "old-remote"u8.ToArray());
        await using var afc = server.CreateClient();

        string localFile = Path.Combine(tempRoot, "existing.txt");
        await File.WriteAllTextAsync(localFile, "new-remote-v1");

        // Push file into existing remote directory -> lands at /dest_dir/existing.txt and overwrites.
        AfcTransferResult pushIntoDir = await afc.PushAsync(localFile, "/dest_dir");
        Assert.Equal("/dest_dir/existing.txt", pushIntoDir.DestinationPath);
        Assert.Equal("new-remote-v1", Encoding.UTF8.GetString(server.GetFileBytes("/dest_dir/existing.txt")));

        // Push file directly onto existing remote file -> overwrites in place.
        await File.WriteAllTextAsync(localFile, "new-remote-v2");
        AfcTransferResult pushOntoFile = await afc.PushAsync(localFile, "/dest_dir/existing.txt");
        Assert.Equal("/dest_dir/existing.txt", pushOntoFile.DestinationPath);
        Assert.Equal("new-remote-v2", Encoding.UTF8.GetString(server.GetFileBytes("/dest_dir/existing.txt")));

        // Push file into nonexistent remote parent fails with ObjectNotFound (matching upstream push).
        var missingParentEx = await Assert.ThrowsAsync<AfcTransferException>(
            () => afc.PushAsync(localFile, "/no_such_parent/file.txt"));
        Assert.Equal(AfcErrorCode.ObjectNotFound, missingParentEx.ErrorCode);

        // Pull single file into existing local directory -> lands at localOut/existing.txt and overwrites.
        string localOutDir = Path.Combine(tempRoot, "out_dir");
        Directory.CreateDirectory(localOutDir);
        string existingLocalTarget = Path.Combine(localOutDir, "existing.txt");
        await File.WriteAllTextAsync(existingLocalTarget, "stale-local");

        AfcTransferResult pullIntoDir = await afc.PullAsync("/dest_dir/existing.txt", localOutDir);
        Assert.Equal(existingLocalTarget, pullIntoDir.DestinationPath);
        Assert.Equal("new-remote-v2", await File.ReadAllTextAsync(existingLocalTarget));

        // Pull single file to explicit local file path -> creates/overwrites at exact path.
        string explicitTarget = Path.Combine(tempRoot, "renamed_out", "custom.txt");
        AfcTransferResult pullExplicit = await afc.PullAsync("/dest_dir/existing.txt", explicitTarget);
        Assert.Equal(explicitTarget, pullExplicit.DestinationPath);
        Assert.Equal("new-remote-v2", await File.ReadAllTextAsync(explicitTarget));

        // Pull remote directory into existing local directory -> lands at out_dir/dest_dir and overwrites matching file.
        string preExistingDir = Path.Combine(localOutDir, "dest_dir");
        Directory.CreateDirectory(preExistingDir);
        await File.WriteAllTextAsync(Path.Combine(preExistingDir, "existing.txt"), "older-copy");
        await File.WriteAllTextAsync(Path.Combine(preExistingDir, "keep_local.txt"), "untouched");

        AfcTransferResult pullDir = await afc.PullAsync("/dest_dir", localOutDir);
        Assert.Equal(preExistingDir, pullDir.DestinationPath);
        Assert.Equal("new-remote-v2", await File.ReadAllTextAsync(Path.Combine(preExistingDir, "existing.txt")));
        Assert.Equal("untouched", await File.ReadAllTextAsync(Path.Combine(preExistingDir, "keep_local.txt")));
    }

    [Fact]
    public async Task LargeMultiChunkFile_StreamsAcrossMultipleDefaultChunksAndMatchesSha256()
    {
        byte[] payload = CreateDeterministicBytes((2 * AfcClient.DefaultTransferChunkSize) + 54_321, seed: 99);
        string srcFile = Path.Combine(tempRoot, "big.dat");
        string dstFile = Path.Combine(tempRoot, "big_pulled.dat");
        await File.WriteAllBytesAsync(srcFile, payload);

        await using var server = new SimulatedAfcServer();
        await using var afc = server.CreateClient();

        var pushProgress = new List<long>();
        await afc.PushAsync(srcFile, "/big.dat", progress: new SyncProgress<AfcTransferProgress>(p =>
        {
            if (p.Stage == AfcTransferStage.FileProgress)
            {
                pushProgress.Add(p.FileBytesTransferred);
            }
        }));

        Assert.Equal(3, server.WriteChunkCount);
        Assert.Equal(3, pushProgress.Count);
        Assert.Equal(payload.Length, pushProgress[^1]);

        var pullProgress = new List<long>();
        await afc.PullAsync("/big.dat", dstFile, progress: new SyncProgress<AfcTransferProgress>(p =>
        {
            if (p.Stage == AfcTransferStage.FileProgress)
            {
                pullProgress.Add(p.FileBytesTransferred);
            }
        }));

        Assert.True(server.ReadChunkCount >= 3);
        Assert.Equal(3, pullProgress.Count);
        Assert.Equal(payload.Length, pullProgress[^1]);
        Assert.Equal(SHA256.HashData(payload), SHA256.HashData(await File.ReadAllBytesAsync(dstFile)));
    }

    [Fact]
    public async Task Pull_FailsByDefaultOnMidTransferError_ReportsCompletedVsIncompleteAndLeavesNoPartialFile()
    {
        byte[] firstBytes = CreateDeterministicBytes(16_384, seed: 1);
        byte[] secondBytes = CreateDeterministicBytes(200_000, seed: 2);
        byte[] thirdBytes = CreateDeterministicBytes(8_192, seed: 3);

        await using var server = new SimulatedAfcServer();
        server.AddFile("/batch/01_first.bin", firstBytes);
        server.AddFile("/batch/02_broken.bin", secondBytes);
        server.AddFile("/batch/03_third.bin", thirdBytes);
        // Fail on the 2nd read chunk of 02_broken.bin so the file has started writing locally before failing.
        server.FailReadOnChunk("/batch/02_broken.bin", chunkIndexOneBased: 2, AfcErrorCode.ReadError);

        await using var afc = server.CreateClient();
        string outDir = Path.Combine(tempRoot, "pull_fail");
        Directory.CreateDirectory(outDir);

        var ex = await Assert.ThrowsAsync<AfcTransferException>(() => afc.PullAsync(
            "/batch",
            outDir,
            new AfcPullOptions { ChunkSize = 64 * 1024, IgnoreErrors = false }));

        Assert.Equal(AfcErrorCode.ReadError, ex.ErrorCode);
        Assert.Equal("02_broken.bin", ex.FailedEntry.RelativePath);
        Assert.Equal("/batch/02_broken.bin", ex.FailedEntry.RemotePath);
        Assert.Single(ex.PartialResult.CompletedFiles);
        Assert.Equal("01_first.bin", ex.PartialResult.CompletedFiles[0].RelativePath);
        Assert.Single(ex.PartialResult.Failures);

        string batchOut = Path.Combine(outDir, "batch");
        Assert.True(File.Exists(Path.Combine(batchOut, "01_first.bin")));
        Assert.Equal(SHA256.HashData(firstBytes), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(batchOut, "01_first.bin"))));
        Assert.False(File.Exists(Path.Combine(batchOut, "02_broken.bin")));
        Assert.False(File.Exists(Path.Combine(batchOut, "03_third.bin")));
        Assert.Empty(Directory.GetFiles(batchOut, "*.afctmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedFileCompletionPreservesDestinationAndRemovesStaging(bool shortRead)
    {
        await using var server = new SimulatedAfcServer();
        server.AddFile("/report", "new"u8.ToArray());
        if (shortRead) server.ReportedFileSize = 8;
        else server.CloseError = AfcErrorCode.ReadError;
        await using var client = server.CreateClient();
        string destination = Path.Combine(tempRoot, "report");
        await File.WriteAllTextAsync(destination, "original");

        await Assert.ThrowsAsync<AfcTransferException>(() => client.PullAsync("/report", destination));

        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Equal(new[] { destination }, Directory.GetFiles(tempRoot));
    }

    [Fact]
    public async Task Pull_WithIgnoreErrors_ContinuesAfterPerFileAndSubdirectoryErrors()
    {
        byte[] good1 = "good-one"u8.ToArray();
        byte[] good2 = "good-two"u8.ToArray();

        await using var server = new SimulatedAfcServer();
        server.AddFile("/media/01_good.txt", good1);
        server.AddFile("/media/02_denied.txt", "secret"u8.ToArray());
        server.AddDirectory("/media/03_locked_dir");
        server.AddFile("/media/03_locked_dir/hidden.txt", "locked"u8.ToArray());
        server.AddFile("/media/04_sub/ok.txt", good2);
        server.FailOpen("/media/02_denied.txt", AfcErrorCode.PermDenied);
        server.FailReadDir("/media/03_locked_dir", AfcErrorCode.PermDenied);

        await using var afc = server.CreateClient();
        string outDir = Path.Combine(tempRoot, "pull_ignore");
        Directory.CreateDirectory(outDir);

        AfcTransferResult result = await afc.PullAsync(
            "/media",
            outDir,
            new AfcPullOptions { IgnoreErrors = true });

        Assert.True(result.HasFailures);
        Assert.Equal(
            ["01_good.txt", "04_sub/ok.txt"],
            result.CompletedFiles.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["02_denied.txt", "03_locked_dir"],
            result.Failures.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.All(result.Failures, f => Assert.Equal(AfcErrorCode.PermDenied, f.ErrorCode));

        string mediaOut = Path.Combine(outDir, "media");
        Assert.Equal(good1, await File.ReadAllBytesAsync(Path.Combine(mediaOut, "01_good.txt")));
        Assert.Equal(good2, await File.ReadAllBytesAsync(Path.Combine(mediaOut, "04_sub", "ok.txt")));
        Assert.False(File.Exists(Path.Combine(mediaOut, "02_denied.txt")));
    }

    [Fact]
    public async Task Pull_CancelledMidChunk_StopsPromptlyCleansTempFileAndClosesAfcTransport()
    {
        byte[] firstBytes = CreateDeterministicBytes(10_000, seed: 11);
        byte[] secondBytes = CreateDeterministicBytes(200_000, seed: 12);
        byte[] thirdBytes = CreateDeterministicBytes(10_000, seed: 13);

        await using var server = new SimulatedAfcServer();
        server.AddFile("/cancel_tree/01_done.bin", firstBytes);
        server.AddFile("/cancel_tree/02_slow.bin", secondBytes);
        server.AddFile("/cancel_tree/03_never.bin", thirdBytes);

        var reachedSecondChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.PauseReadOnChunk("/cancel_tree/02_slow.bin", chunkIndexOneBased: 2, reachedSecondChunk, releaseServer);

        await using var afc = server.CreateClient();
        string outDir = Path.Combine(tempRoot, "pull_cancel");
        Directory.CreateDirectory(outDir);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var completedViaProgress = new List<string>();
        Task<AfcTransferResult> pullTask = afc.PullAsync(
            "/cancel_tree",
            outDir,
            new AfcPullOptions { ChunkSize = 64 * 1024, IgnoreErrors = true },
            new SyncProgress<AfcTransferProgress>(p =>
            {
                if (p.Stage == AfcTransferStage.FileCompleted)
                {
                    completedViaProgress.Add(p.RelativePath);
                }
            }),
            cts.Token);

        await reachedSecondChunk.Task.WaitAsync(timeout.Token);
        cts.Cancel();
        try
        {
            // Keep the reply pending until cancellation is observed inside the exchange.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pullTask.WaitAsync(timeout.Token));
        }
        finally
        {
            releaseServer.TrySetResult();
        }

        // Underlying AfcClient cancellation semantics must remain intact (in-flight cancellation closes transport).
        await Assert.ThrowsAsync<ObjectDisposedException>(() => afc.GetDeviceInfoAsync(timeout.Token));

        string treeOut = Path.Combine(outDir, "cancel_tree");
        Assert.Equal(["01_done.bin"], completedViaProgress);
        Assert.True(File.Exists(Path.Combine(treeOut, "01_done.bin")));
        Assert.Equal(SHA256.HashData(firstBytes), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(treeOut, "01_done.bin"))));
        Assert.False(File.Exists(Path.Combine(treeOut, "02_slow.bin")));
        Assert.False(File.Exists(Path.Combine(treeOut, "03_never.bin")));
        Assert.Empty(Directory.GetFiles(treeOut, "*.afctmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Push_CancelledMidChunk_StopsPromptlyAndClosesAfcTransport()
    {
        string srcTree = Path.Combine(tempRoot, "push_cancel_src");
        Directory.CreateDirectory(srcTree);
        await File.WriteAllBytesAsync(Path.Combine(srcTree, "01_done.bin"), CreateDeterministicBytes(8_000, seed: 21));
        await File.WriteAllBytesAsync(Path.Combine(srcTree, "02_slow.bin"), CreateDeterministicBytes(200_000, seed: 22));
        await File.WriteAllBytesAsync(Path.Combine(srcTree, "03_never.bin"), CreateDeterministicBytes(8_000, seed: 23));

        await using var server = new SimulatedAfcServer();
        var reachedSecondWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.PauseWriteOnChunk("/upload/push_cancel_src/02_slow.bin", chunkIndexOneBased: 2, reachedSecondWrite, releaseServer);

        await using var afc = server.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var completedViaProgress = new List<string>();
        Task<AfcTransferResult> pushTask = afc.PushAsync(
            srcTree,
            "/upload",
            new AfcPushOptions { ChunkSize = 64 * 1024 },
            new SyncProgress<AfcTransferProgress>(p =>
            {
                if (p.Stage == AfcTransferStage.FileCompleted)
                {
                    completedViaProgress.Add(p.RelativePath);
                }
            }),
            cts.Token);

        await reachedSecondWrite.Task.WaitAsync(timeout.Token);
        cts.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pushTask.WaitAsync(timeout.Token));
        }
        finally
        {
            releaseServer.TrySetResult();
        }
        await Assert.ThrowsAsync<ObjectDisposedException>(() => afc.GetDeviceInfoAsync(timeout.Token));

        Assert.Equal(["01_done.bin"], completedViaProgress);
        Assert.False(server.FileExists("/upload/push_cancel_src/03_never.bin"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    [InlineData("/abs/escaped.txt")]
    [InlineData("sub/escaped.txt")]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("C:evil.txt")]
    public async Task Pull_RejectsPathTraversalAndReservedDeviceNames_NeverWritesOutsideDestination(string maliciousEntry)
    {
        await using var server = new SimulatedAfcServer();
        server.AddDirectory("/exfil");
        server.AddFile("/exfil/good.txt", "safe-content"u8.ToArray());
        // Raw listing omits "." when testing lone "..", or injects malicious name alongside good.txt.
        string[] rawListing = maliciousEntry == ".."
            ? ["..", "good.txt"]
            : [".", "..", maliciousEntry, "good.txt"];
        server.OverrideRawDirectoryListing("/exfil", rawListing);

        await using var afc = server.CreateClient();
        string parentGuard = Path.Combine(tempRoot, $"guard_{Guid.NewGuid():N}");
        string outDir = Path.Combine(parentGuard, "out");
        Directory.CreateDirectory(outDir);

        // Default IgnoreErrors = false must fail with InvalidDataException inside AfcTransferException.
        var ex = await Assert.ThrowsAsync<AfcTransferException>(() => afc.PullAsync("/exfil", outDir));
        Assert.IsType<InvalidDataException>(ex.InnerException);
        Assert.False(File.Exists(Path.Combine(parentGuard, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(outDir, "escaped.txt")));

        // With IgnoreErrors = true, malicious entry is skipped and recorded in Failures, nothing escapes, good.txt still downloads.
        AfcTransferResult bestEffort = await afc.PullAsync(
            "/exfil",
            outDir,
            new AfcPullOptions { IgnoreErrors = true });

        Assert.True(bestEffort.HasFailures);
        Assert.Contains(bestEffort.Failures, f => f.Exception is InvalidDataException);
        Assert.Contains(bestEffort.CompletedFiles, f => f.RelativePath == "good.txt");
        Assert.Equal("safe-content", await File.ReadAllTextAsync(Path.Combine(outDir, "exfil", "good.txt")));
        Assert.False(File.Exists(Path.Combine(parentGuard, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(outDir, "escaped.txt")));
    }

    [Fact]
    public async Task Symlinks_PreventsTraversalCyclesInListAndPull_AndDownloadsValidFileSymlink()
    {
        byte[] realContent = "symlink-target-payload"u8.ToArray();

        await using var server = new SimulatedAfcServer();
        server.AddFile("/symroot/real.txt", realContent);
        server.AddSymlink("/symroot/alias.txt", "real.txt");
        server.AddSymlink("/symroot/self_loop", ".");
        server.AddDirectory("/symroot/sub");
        server.AddSymlink("/symroot/sub/parent_loop", "..");
        server.AddSymlink("/symroot/chain_a", "chain_b");
        server.AddSymlink("/symroot/chain_b", "chain_a");

        await using var afc = server.CreateClient();

        // 1. Recursive listing must terminate without following symlinked directories into a cycle.
        IReadOnlyList<AfcDirectoryEntry> listed = await afc.ListDirectoryAsync("/symroot", recursive: true);
        Assert.Contains(listed, e => e.RelativePath == "real.txt" && e.IsRegularFile);
        Assert.Contains(listed, e => e.RelativePath == "alias.txt" && e.IsSymbolicLink && e.Info.LinkTarget == "real.txt");
        Assert.Contains(listed, e => e.RelativePath == "self_loop" && e.IsSymbolicLink);
        Assert.Contains(listed, e => e.RelativePath == "sub/parent_loop" && e.IsSymbolicLink);

        // 2. Pull with default IgnoreErrors=false rejects symlink cycles instead of looping.
        string strictOut = Path.Combine(tempRoot, "sym_strict");
        Directory.CreateDirectory(strictOut);
        var cycleEx = await Assert.ThrowsAsync<AfcTransferException>(() => afc.PullAsync("/symroot", strictOut));
        Assert.IsType<InvalidDataException>(cycleEx.InnerException);

        // 3. Pull with IgnoreErrors=true downloads valid files & file symlinks, skips cyclic links, and terminates.
        string bestEffortOut = Path.Combine(tempRoot, "sym_best");
        Directory.CreateDirectory(bestEffortOut);
        AfcTransferResult result = await afc.PullAsync(
            "/symroot",
            bestEffortOut,
            new AfcPullOptions { IgnoreErrors = true });

        Assert.Equal(
            ["alias.txt", "real.txt"],
            result.CompletedFiles.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["chain_a", "chain_b", "self_loop", "sub/parent_loop"],
            result.Failures.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.All(result.Failures, f => Assert.IsType<InvalidDataException>(f.Exception));

        string symOutRoot = Path.Combine(bestEffortOut, "symroot");
        Assert.Equal(realContent, await File.ReadAllBytesAsync(Path.Combine(symOutRoot, "real.txt")));
        Assert.Equal(realContent, await File.ReadAllBytesAsync(Path.Combine(symOutRoot, "alias.txt")));
    }

    [Fact]
    public async Task ListDirectory_NonRecursive_DoesNotReadUnreadableSubdirectories()
    {
        await using var server = new SimulatedAfcServer();
        server.AddDirectory("/root/protected");
        server.AddFile("/root/top.txt", "hi"u8.ToArray());
        server.FailReadDir("/root/protected", AfcErrorCode.PermDenied);

        await using var afc = server.CreateClient();

        IReadOnlyList<AfcDirectoryEntry> entries = await afc.ListDirectoryAsync("/root", recursive: false);
        Assert.Equal(
            ["protected", "top.txt"],
            entries.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("/root/protected", server.ReadDirectoriesCalled);
    }

    private static byte[] CreateDeterministicBytes(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>
    /// Full-duplex in-memory AFC server speaking the real binary AFC wire protocol over a Stream,
    /// allowing end-to-end testing of AfcClient without hardware.
    /// </summary>
    private sealed class SimulatedAfcServer : IAsyncDisposable
    {
        private const int HeaderSize = 40;
        private static ReadOnlySpan<byte> Magic => "CFA6LPAA"u8;

        private readonly DuplexPipeStream clientStream;
        private readonly DuplexPipeStream serverStream;
        private readonly CancellationTokenSource serverCts = new();
        private readonly Task serverLoop;

        private readonly Dictionary<string, FakeNode> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> rawDirOverrides = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AfcErrorCode> openFailures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AfcErrorCode> readDirFailures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int ChunkIndex, AfcErrorCode Error)> readChunkFailures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int ChunkIndex, TaskCompletionSource Reached, TaskCompletionSource Release)> readPauses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int ChunkIndex, TaskCompletionSource Reached, TaskCompletionSource Release)> writePauses = new(StringComparer.Ordinal);
        private readonly Dictionary<ulong, OpenHandle> handles = [];
        private ulong nextHandle = 1;

        public int? ReportedFileSize { get; set; }
        public AfcErrorCode CloseError { get; set; } = AfcErrorCode.Success;
        public int ReadChunkCount { get; private set; }
        public int WriteChunkCount { get; private set; }
        public List<string> ReadDirectoriesCalled { get; } = [];

        public SimulatedAfcServer()
        {
            nodes["/"] = FakeNode.Directory();
            (clientStream, serverStream) = DuplexPipeStream.CreatePair();
            serverLoop = Task.Run(() => ServeAsync(serverCts.Token));
        }

        public AfcClient CreateClient() => new(clientStream);

        public void AddDirectory(string path)
        {
            string normalized = Canonical(path);
            EnsureParentDirs(normalized);
            nodes[normalized] = FakeNode.Directory();
        }

        public void AddFile(string path, byte[] data, DateTimeOffset? mtime = null)
        {
            string normalized = Canonical(path);
            EnsureParentDirs(normalized);
            nodes[normalized] = FakeNode.File(data.ToArray(), mtime ?? DateTimeOffset.UtcNow);
        }

        public void AddSymlink(string path, string linkTarget)
        {
            string normalized = Canonical(path);
            EnsureParentDirs(normalized);
            nodes[normalized] = FakeNode.Symlink(linkTarget);
        }

        public void OverrideRawDirectoryListing(string path, string[] entries) =>
            rawDirOverrides[Canonical(path)] = entries;

        public void FailOpen(string path, AfcErrorCode error) =>
            openFailures[Canonical(path)] = error;

        public void FailReadDir(string path, AfcErrorCode error) =>
            readDirFailures[Canonical(path)] = error;

        public void FailReadOnChunk(string path, int chunkIndexOneBased, AfcErrorCode error) =>
            readChunkFailures[Canonical(path)] = (chunkIndexOneBased, error);

        public void PauseReadOnChunk(string path, int chunkIndexOneBased, TaskCompletionSource reached, TaskCompletionSource release) =>
            readPauses[Canonical(path)] = (chunkIndexOneBased, reached, release);

        public void PauseWriteOnChunk(string path, int chunkIndexOneBased, TaskCompletionSource reached, TaskCompletionSource release) =>
            writePauses[Canonical(path)] = (chunkIndexOneBased, reached, release);

        public bool FileExists(string path) =>
            nodes.TryGetValue(Canonical(path), out FakeNode? n) && n.Kind == NodeKind.File;

        public byte[] GetFileBytes(string path) =>
            nodes[Canonical(path)].Data;

        private void EnsureParentDirs(string canonicalPath)
        {
            if (canonicalPath == "/")
            {
                return;
            }

            string[] parts = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string current = string.Empty;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current += "/" + parts[i];
                if (!nodes.ContainsKey(current))
                {
                    nodes[current] = FakeNode.Directory();
                }
            }
        }

        private async Task ServeAsync(CancellationToken ct)
        {
            try
            {
                byte[] header = new byte[HeaderSize];
                while (!ct.IsCancellationRequested)
                {
                    int got = await ReadExactOrEofAsync(serverStream, header, ct).ConfigureAwait(false);
                    if (got == 0)
                    {
                        break;
                    }

                    ulong entireLen = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8, 8));
                    ulong thisLen = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16, 8));
                    ulong packetNum = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24, 8));
                    var op = (AfcOperation)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32, 8));

                    int headerPayloadLen = checked((int)(thisLen - HeaderSize));
                    int bodyLen = checked((int)(entireLen - thisLen));

                    byte[] headerPayload = headerPayloadLen > 0 ? new byte[headerPayloadLen] : [];
                    if (headerPayloadLen > 0)
                    {
                        await serverStream.ReadExactlyAsync(headerPayload, ct).ConfigureAwait(false);
                    }

                    byte[] body = bodyLen > 0 ? new byte[bodyLen] : [];
                    if (bodyLen > 0)
                    {
                        await serverStream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
                    }

                    (AfcOperation replyOp, byte[] replyHeader, byte[] replyBody) =
                        await HandlePacketAsync(op, headerPayload, body, ct).ConfigureAwait(false);

                    byte[] reply = BuildPacket(packetNum, replyOp, replyHeader, replyBody);
                    await serverStream.WriteAsync(reply, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (EndOfStreamException) { }
            catch (IOException) { }
        }

        private async Task<(AfcOperation Op, byte[] HeaderPayload, byte[] Body)> HandlePacketAsync(
            AfcOperation op,
            byte[] headerPayload,
            byte[] body,
            CancellationToken ct)
        {
            switch (op)
            {
                case AfcOperation.GetDeviceInfo:
                    return (AfcOperation.Data, [], EncodeNullSeparated(["Model", "iPhone-Simulated", "FSTotalBytes", "1000000000"]));

                case AfcOperation.GetFileInfo:
                {
                    string path = Canonical(DecodeCString(headerPayload));
                    if (!nodes.TryGetValue(path, out FakeNode? node))
                    {
                        return Status(AfcErrorCode.ObjectNotFound);
                    }

                    long mtimeNs = node.ModifyTime.ToUnixTimeMilliseconds() * 1_000_000L;
                    var kv = new List<string>
                    {
                        "st_size", node.Kind == NodeKind.File ? (ReportedFileSize ?? node.Data.Length).ToString(CultureInfo.InvariantCulture) : "64",
                        "st_blocks", "8",
                        "st_nlink", "1",
                        "st_ifmt", node.Kind switch
                        {
                            NodeKind.Directory => "S_IFDIR",
                            NodeKind.File => "S_IFREG",
                            NodeKind.Symlink => "S_IFLNK",
                            _ => "S_IFREG",
                        },
                        "st_birthtime", mtimeNs.ToString(CultureInfo.InvariantCulture),
                        "st_mtime", mtimeNs.ToString(CultureInfo.InvariantCulture),
                    };
                    if (node.Kind == NodeKind.Symlink && node.LinkTarget is not null)
                    {
                        kv.Add("LinkTarget");
                        kv.Add(node.LinkTarget);
                    }

                    return (AfcOperation.Data, [], EncodeNullSeparated(kv));
                }

                case AfcOperation.ReadDir:
                {
                    string path = Canonical(DecodeCString(headerPayload));
                    ReadDirectoriesCalled.Add(path);
                    if (readDirFailures.TryGetValue(path, out AfcErrorCode err))
                    {
                        return Status(err);
                    }

                    if (!nodes.TryGetValue(path, out FakeNode? node))
                    {
                        return Status(AfcErrorCode.ObjectNotFound);
                    }

                    if (node.Kind != NodeKind.Directory)
                    {
                        return Status(AfcErrorCode.ReadError);
                    }

                    if (rawDirOverrides.TryGetValue(path, out string[]? custom))
                    {
                        return (AfcOperation.Data, [], EncodeNullSeparated(custom));
                    }

                    string prefix = path == "/" ? "/" : path + "/";
                    var names = new List<string> { ".", ".." };
                    foreach (string key in nodes.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (key == path || !key.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string remainder = key[prefix.Length..];
                        if (!remainder.Contains('/'))
                        {
                            names.Add(remainder);
                        }
                    }

                    return (AfcOperation.Data, [], EncodeNullSeparated(names));
                }

                case AfcOperation.MakeDir:
                {
                    string path = Canonical(DecodeCString(headerPayload));
                    if (nodes.TryGetValue(path, out FakeNode? existing))
                    {
                        return existing.Kind == NodeKind.Directory
                            ? Status(AfcErrorCode.Success)
                            : Status(AfcErrorCode.ObjectExists);
                    }

                    EnsureParentDirs(path);
                    nodes[path] = FakeNode.Directory();
                    return Status(AfcErrorCode.Success);
                }

                case AfcOperation.FileRefOpen:
                {
                    var mode = (AfcFileOpenMode)BinaryPrimitives.ReadUInt64LittleEndian(headerPayload.AsSpan(0, 8));
                    string path = Canonical(DecodeCString(headerPayload.AsSpan(8)));
                    if (openFailures.TryGetValue(path, out AfcErrorCode openErr))
                    {
                        return Status(openErr);
                    }

                    if (mode == AfcFileOpenMode.ReadOnly)
                    {
                        if (!nodes.TryGetValue(path, out FakeNode? node))
                        {
                            return Status(AfcErrorCode.ObjectNotFound);
                        }

                        if (node.Kind == NodeKind.Directory)
                        {
                            return Status(AfcErrorCode.ObjectIsDir);
                        }

                        ulong h = nextHandle++;
                        handles[h] = new OpenHandle(path, mode, new MemoryStream(node.Data, writable: false));
                        return (AfcOperation.FileRefOpenResult, BitConverter.GetBytes(h), []);
                    }

                    if (mode is AfcFileOpenMode.WriteOnly or AfcFileOpenMode.WriteRead)
                    {
                        string parent = GetParent(path);
                        if (!nodes.TryGetValue(parent, out FakeNode? parentNode) || parentNode.Kind != NodeKind.Directory)
                        {
                            return Status(AfcErrorCode.ObjectNotFound);
                        }

                        if (nodes.TryGetValue(path, out FakeNode? existing) && existing.Kind == NodeKind.Directory)
                        {
                            return Status(AfcErrorCode.ObjectIsDir);
                        }

                        ulong h = nextHandle++;
                        handles[h] = new OpenHandle(path, mode, new MemoryStream());
                        return (AfcOperation.FileRefOpenResult, BitConverter.GetBytes(h), []);
                    }

                    return Status(AfcErrorCode.OpNotSupported);
                }

                case AfcOperation.FileRefRead:
                {
                    ulong h = BinaryPrimitives.ReadUInt64LittleEndian(headerPayload.AsSpan(0, 8));
                    int maxBytes = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(headerPayload.AsSpan(8, 8)));
                    if (!handles.TryGetValue(h, out OpenHandle? open))
                    {
                        return Status(AfcErrorCode.InvalidArg);
                    }

                    open.ChunkCounter++;
                    if (readPauses.TryGetValue(open.Path, out var pause) && pause.ChunkIndex == open.ChunkCounter)
                    {
                        pause.Reached.TrySetResult();
                        await pause.Release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }

                    if (readChunkFailures.TryGetValue(open.Path, out var fail) && fail.ChunkIndex == open.ChunkCounter)
                    {
                        return Status(fail.Error);
                    }

                    byte[] buf = new byte[maxBytes];
                    int read = open.Buffer.Read(buf, 0, maxBytes);
                    if (read > 0)
                    {
                        ReadChunkCount++;
                    }

                    return (AfcOperation.Data, [], read == buf.Length ? buf : buf[..read]);
                }

                case AfcOperation.FileRefWrite:
                {
                    ulong h = BinaryPrimitives.ReadUInt64LittleEndian(headerPayload.AsSpan(0, 8));
                    if (!handles.TryGetValue(h, out OpenHandle? open))
                    {
                        return Status(AfcErrorCode.InvalidArg);
                    }

                    open.ChunkCounter++;
                    if (writePauses.TryGetValue(open.Path, out var pause) && pause.ChunkIndex == open.ChunkCounter)
                    {
                        pause.Reached.TrySetResult();
                        await pause.Release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }

                    open.Buffer.Write(body, 0, body.Length);
                    WriteChunkCount++;
                    return Status(AfcErrorCode.Success);
                }

                case AfcOperation.FileRefClose:
                {
                    ulong h = BinaryPrimitives.ReadUInt64LittleEndian(headerPayload.AsSpan(0, 8));
                    if (!handles.Remove(h, out OpenHandle? open))
                    {
                        return Status(AfcErrorCode.InvalidArg);
                    }

                    if (open.Mode is AfcFileOpenMode.WriteOnly or AfcFileOpenMode.WriteRead)
                    {
                        nodes[open.Path] = FakeNode.File(open.Buffer.ToArray(), DateTimeOffset.UtcNow);
                    }

                    open.Buffer.Dispose();
                    return Status(CloseError);
                }

                default:
                    return Status(AfcErrorCode.OpNotSupported);
            }
        }

        private static (AfcOperation, byte[], byte[]) Status(AfcErrorCode code) =>
            (AfcOperation.Status, BitConverter.GetBytes((ulong)code), []);

        private static byte[] BuildPacket(ulong packetNum, AfcOperation op, ReadOnlySpan<byte> headerPayload, ReadOnlySpan<byte> body)
        {
            ulong thisLen = (ulong)(HeaderSize + headerPayload.Length);
            ulong entireLen = thisLen + (ulong)body.Length;
            byte[] buf = new byte[entireLen];
            Magic.CopyTo(buf.AsSpan(0, 8));
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8, 8), entireLen);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(16, 8), thisLen);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(24, 8), packetNum);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(32, 8), (ulong)op);
            headerPayload.CopyTo(buf.AsSpan(HeaderSize));
            body.CopyTo(buf.AsSpan(HeaderSize + headerPayload.Length));
            return buf;
        }

        private static string DecodeCString(ReadOnlySpan<byte> bytes)
        {
            int nul = bytes.IndexOf((byte)0);
            return Encoding.UTF8.GetString(nul >= 0 ? bytes[..nul] : bytes);
        }

        private static byte[] EncodeNullSeparated(IEnumerable<string> items)
        {
            using var ms = new MemoryStream();
            foreach (string item in items)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(item);
                ms.Write(utf8, 0, utf8.Length);
                ms.WriteByte(0);
            }

            return ms.ToArray();
        }

        private static string Canonical(string path) => AfcClient.CanonicalizeRemotePath(path);

        private static string GetParent(string canonicalPath)
        {
            int idx = canonicalPath.LastIndexOf('/');
            return idx <= 0 ? "/" : canonicalPath[..idx];
        }

        private static async Task<int> ReadExactOrEofAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    if (total == 0)
                    {
                        return 0;
                    }

                    throw new EndOfStreamException();
                }

                total += n;
            }

            return total;
        }

        public async ValueTask DisposeAsync()
        {
            serverCts.Cancel();
            await clientStream.DisposeAsync().ConfigureAwait(false);
            await serverStream.DisposeAsync().ConfigureAwait(false);
            try { await serverLoop.ConfigureAwait(false); } catch { }
            serverCts.Dispose();
        }

        private enum NodeKind { Directory, File, Symlink }

        private sealed class FakeNode
        {
            public NodeKind Kind { get; init; }
            public byte[] Data { get; init; } = [];
            public string? LinkTarget { get; init; }
            public DateTimeOffset ModifyTime { get; init; } = DateTimeOffset.UtcNow;

            public static FakeNode Directory() => new() { Kind = NodeKind.Directory };
            public static FakeNode File(byte[] data, DateTimeOffset mtime) => new() { Kind = NodeKind.File, Data = data, ModifyTime = mtime };
            public static FakeNode Symlink(string target) => new() { Kind = NodeKind.Symlink, LinkTarget = target };
        }

        private sealed class OpenHandle(string path, AfcFileOpenMode mode, MemoryStream buffer)
        {
            public string Path { get; } = path;
            public AfcFileOpenMode Mode { get; } = mode;
            public MemoryStream Buffer { get; } = buffer;
            public int ChunkCounter { get; set; }
        }
    }

    private sealed class DuplexPipeStream : Stream
    {
        private readonly ByteChannel inbound;
        private readonly ByteChannel outbound;
        private int disposed;

        private DuplexPipeStream(ByteChannel inbound, ByteChannel outbound)
        {
            this.inbound = inbound;
            this.outbound = outbound;
        }

        public static (DuplexPipeStream A, DuplexPipeStream B) CreatePair()
        {
            var aToB = new ByteChannel();
            var bToA = new ByteChannel();
            return (new DuplexPipeStream(bToA, aToB), new DuplexPipeStream(aToB, bToA));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return inbound.ReadAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            outbound.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                outbound.Complete();
                inbound.Complete();
            }

            base.Dispose(disposing);
        }

        private sealed class ByteChannel
        {
            private readonly ConcurrentQueue<byte[]> chunks = new();
            private readonly SemaphoreSlim available = new(0);
            private byte[]? current;
            private int currentOffset;
            private volatile bool completed;

            public void Write(ReadOnlySpan<byte> data)
            {
                if (completed || data.IsEmpty)
                {
                    return;
                }

                chunks.Enqueue(data.ToArray());
                available.Release();
            }

            public void Complete()
            {
                if (!completed)
                {
                    completed = true;
                    available.Release();
                }
            }

            public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
            {
                while (true)
                {
                    if (current is not null && currentOffset < current.Length)
                    {
                        int toCopy = Math.Min(destination.Length, current.Length - currentOffset);
                        current.AsSpan(currentOffset, toCopy).CopyTo(destination.Span);
                        currentOffset += toCopy;
                        if (currentOffset >= current.Length)
                        {
                            current = null;
                            currentOffset = 0;
                        }

                        return toCopy;
                    }

                    if (chunks.TryDequeue(out byte[]? next))
                    {
                        current = next;
                        currentOffset = 0;
                        continue;
                    }

                    if (completed)
                    {
                        return 0;
                    }

                    await available.WaitAsync(ct).ConfigureAwait(false);
                }
            }
        }
    }
}
