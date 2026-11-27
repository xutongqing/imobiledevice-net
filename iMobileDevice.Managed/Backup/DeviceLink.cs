namespace iMobileDevice.Managed.Backup;

using System.Text;
using iMobileDevice.Managed.Plist;

internal sealed class DeviceLink(Stream stream, string root)
{
    private const string Empty = "___EmptyParameterString___";
    private const byte Data = 0x0c, RemoteError = 0x0b, LocalError = 0x06;
    private readonly BackupWire wire = new(stream);
    private readonly byte[] buffer = new byte[32 * 1024];
    private readonly byte[] codeByte = new byte[1];
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public async Task HandshakeAsync(CancellationToken token)
    {
        var offer = await ReceiveAsync(token).ConfigureAwait(false);
        if (offer.Count < 3 || offer[0].AsString() != "DLMessageVersionExchange" || offer[1] is not PlistInteger)
            throw new InvalidDataException("Expected DeviceLink version exchange.");
        await wire.SendAsync(new PlistArray { "DLMessageVersionExchange", "DLVersionsOk", offer[1] }, token).ConfigureAwait(false);
        if ((await ReceiveAsync(token).ConfigureAwait(false))[0].AsString() != "DLMessageDeviceReady")
            throw new InvalidDataException("Expected DeviceLink device ready.");
        await ProcessAsync(new() { ["MessageName"] = "Hello", ["SupportedProtocolVersions"] = new PlistArray { 2.0, 2.1 } }, token).ConfigureAwait(false);
        var response = Result(await ReceiveAsync(token).ConfigureAwait(false));
        if (!response.TryGetValue("ProtocolVersion", out var version) || Numeric(version) is not (2.0 or 2.1))
            throw new InvalidDataException("The device selected an unsupported MobileBackup2 version.");
    }

    private async Task<PlistArray> ReceiveAsync(CancellationToken token)
    {
        if (await wire.ReceiveAsync(token).ConfigureAwait(false) is not PlistArray { Count: > 0 } message)
            throw new InvalidDataException("Expected a DeviceLink message.");
        return message;
    }

    private static double Numeric(PlistNode value) => value switch
    {
        PlistReal r => r.Value,
        PlistInteger i => i.Int64Value,
        _ => double.NaN,
    };

    private static PlistDictionary Result(PlistArray message)
    {
        if (message.Count < 2 || message[0].AsString() != "DLMessageProcessMessage" || message[1] is not PlistDictionary reply ||
            reply.GetInt64("ErrorCode") is not long code)
            throw new InvalidDataException("Missing MobileBackup2 operation result.");
        if (code != 0) throw new MobileBackupException(code, reply.GetString("ErrorDescription"));
        return reply;
    }

    public Task ProcessAsync(PlistDictionary request, CancellationToken token) =>
        wire.SendAsync(new PlistArray { "DLMessageProcessMessage", request }, token);

    private Task StatusAsync(long code, string description, PlistNode payload, CancellationToken token) =>
        wire.SendAsync(new PlistArray { "DLMessageStatusResponse", code, description, payload }, token);

    public Task DisconnectAsync(CancellationToken token) =>
        wire.SendAsync(new PlistArray { "DLMessageDisconnect", Empty }, token);

    public async Task<PlistNode?> RunAsync(IProgress<double>? progress, CancellationToken token)
    {
        while (true)
        {
            var msg = await ReceiveAsync(token).ConfigureAwait(false);
            string command = msg[0].AsString();
            int position = command switch
            {
                "DLMessageUploadFiles" => 2,
                "DLMessageDownloadFiles" or "DLMessageRemoveFiles" or "DLMessageRemoveItems" or "DLMessageMoveFiles" or "DLMessageMoveItems" => 3,
                _ => -1,
            };
            if (progress != null && position >= 0 && position < msg.Count && Numeric(msg[position]) is >= 0 and <= 100)
                progress.Report(Numeric(msg[position]));

            switch (command)
            {
                case "DLMessageProcessMessage":
                    return Result(msg).TryGetValue("Content", out var content) ? content : null;
                case "DLMessageDisconnect":
                    throw new EndOfStreamException("Device disconnected before reporting a backup result.");
                case "DLMessageUploadFiles":
                    await UploadAsync(token).ConfigureAwait(false);
                    break;
                case "DLMessageDownloadFiles":
                    await DownloadAsync(msg[1].AsArray(), token).ConfigureAwait(false);
                    break;
                case "DLMessagePurgeDiskSpace":
                    await StatusAsync(-1, "DLPurgeDiskSpace failed to purge", 0, token).ConfigureAwait(false);
                    break;
                default:
                    await FileCommandAsync(command, msg, token).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task FileCommandAsync(string command, PlistArray msg, CancellationToken token)
    {
        PlistNode result = new PlistDictionary();
        try
        {
            switch (command)
            {
                case "DLMessageGetFreeDiskSpace":
                    var drive = DriveInfo.GetDrives().Where(d => BackupStorage.ContainsPath(d.Name, root))
                        .MaxBy(d => d.Name.Length) ?? new DriveInfo(Path.GetPathRoot(root)!);
                    result = drive.AvailableFreeSpace;
                    break;
                case "DLMessageCreateDirectory":
                    Directory.CreateDirectory(BackupStorage.Resolve(root, msg[1].AsString(), true));
                    break;
                case "DLContentsOfDirectory":
                    var entries = new PlistDictionary();
                    foreach (var path in Directory.EnumerateFileSystemEntries(BackupStorage.Resolve(root, msg[1].AsString(), true)))
                    {
                        token.ThrowIfCancellationRequested();
                        BackupStorage.Resolve(root, Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
                        var entry = new FileInfo(path);
                        bool directory = entry.Attributes.HasFlag(FileAttributes.Directory);
                        entries[entry.Name] = new PlistDictionary
                        {
                            ["DLFileType"] = directory ? "DLFileTypeDirectory" : "DLFileTypeRegular",
                            ["DLFileSize"] = directory ? 0L : entry.Length,
                            ["DLFileModificationDate"] = new DateTimeOffset(entry.LastWriteTimeUtc),
                        };
                    }
                    result = entries;
                    break;
                case "DLMessageRemoveItems":
                case "DLMessageRemoveFiles":
                    foreach (var item in msg[1].AsArray())
                    {
                        token.ThrowIfCancellationRequested();
                        string path = BackupStorage.Resolve(root, item.AsString());
                        if (Directory.Exists(path)) Directory.Delete(path, true);
                        else if (File.Exists(path)) File.Delete(path);
                    }
                    break;
                case "DLMessageMoveItems":
                case "DLMessageMoveFiles":
                    foreach (var (from, to) in msg[1].AsDictionary())
                    {
                        token.ThrowIfCancellationRequested();
                        string source = BackupStorage.Resolve(root, from);
                        string dest = BackupStorage.Resolve(root, to.AsString());
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        if (Path.GetRelativePath(source, dest) == ".") continue;
                        if (BackupStorage.ContainsPath(source, dest) || BackupStorage.ContainsPath(dest, source))
                            throw new IOException("Cannot move between overlapping paths.");
                        bool directory = Directory.Exists(source);
                        if (!directory && !File.Exists(source)) throw new FileNotFoundException("Backup item is missing.", source);
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        if (directory)
                        {
                            if (File.Exists(dest)) File.Delete(dest);
                            Directory.Move(source, dest);
                        }
                        else File.Move(source, dest, true);
                    }
                    break;
                case "DLMessageCopyItem":
                    await BackupStorage.CopyAsync(BackupStorage.Resolve(root, msg[1].AsString()),
                        BackupStorage.Resolve(root, msg[2].AsString()), token).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidDataException("Unsupported DeviceLink command: " + command);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await StatusAsync(FileError(ex), ex.Message, new PlistDictionary(), token).ConfigureAwait(false);
            return;
        }
        await StatusAsync(0, Empty, result, token).ConfigureAwait(false);
    }

    private static int FileError(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => -6,
        IOException io when (io.HResult & 0xffff) is 28 or 39 or 112 => -15,
        _ => -1,
    };

    private async Task<string> ReadNameAsync(CancellationToken token)
    {
        uint length = await wire.ReadLengthAsync(token).ConfigureAwait(false);
        return Utf8.GetString(await wire.ReadBytesAsync(length, 65536, token).ConfigureAwait(false));
    }

    private async Task UploadAsync(CancellationToken token)
    {
        while ((await ReadNameAsync(token).ConfigureAwait(false)).Length != 0)
        {
            string dest = BackupStorage.Resolve(root, await ReadNameAsync(token).ConfigureAwait(false));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // All transfers are inside an unpublished working snapshot.
            await using var output = File.Create(dest);
            bool receivedData = false;
            while (true)
            {
                uint size = await wire.ReadLengthAsync(token).ConfigureAwait(false);
                if (size == 0) throw new InvalidDataException("Missing file transfer code.");
                await stream.ReadExactlyAsync(codeByte, token).ConfigureAwait(false);
                uint remaining = size - 1;
                if (codeByte[0] == Data)
                {
                    receivedData = true;
                    while (remaining > 0)
                    {
                        int count = (int)Math.Min(remaining, (uint)buffer.Length);
                        await stream.ReadExactlyAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        remaining -= (uint)count;
                    }
                    continue;
                }
                if (codeByte[0] == 0 && remaining == 0) break;
                if (codeByte[0] != RemoteError) throw new InvalidDataException("Unexpected file transfer code.");
                byte[] detail = await wire.ReadBytesAsync(remaining, 65536, token).ConfigureAwait(false);
                if (receivedData && detail.Length != 0) throw new IOException("Device interrupted a backup file: " + Utf8.GetString(detail));
                if (!receivedData)
                {
                    await output.DisposeAsync().ConfigureAwait(false);
                    File.Delete(dest);
                }
                break;
            }
        }
        await StatusAsync(0, Empty, new PlistDictionary(), token).ConfigureAwait(false);
    }

    private async Task DownloadAsync(PlistArray files, CancellationToken token)
    {
        var errors = new PlistDictionary();
        foreach (var item in files)
        {
            string name = item.AsString();
            string path = BackupStorage.Resolve(root, name);
            await wire.WriteBlockAsync(Utf8.GetBytes(name), null, token).ConfigureAwait(false);
            FileStream? input = null;
            Exception? error = null;
            try { input = File.OpenRead(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex; }
            if (input != null)
            {
                await using (input)
                {
                    while (true)
                    {
                        int count;
                        try { count = await input.ReadAsync(buffer, token).ConfigureAwait(false); }
                        catch (IOException ex) { error = ex; break; }
                        if (count == 0) break;
                        await wire.WriteBlockAsync(buffer.AsMemory(0, count), Data, token).ConfigureAwait(false);
                    }
                }
            }
            if (error == null)
                await wire.WriteBlockAsync(ReadOnlyMemory<byte>.Empty, 0, token).ConfigureAwait(false);
            else
            {
                errors[name] = new PlistDictionary { ["DLFileErrorCode"] = FileError(error), ["DLFileErrorString"] = error.Message };
                await wire.WriteBlockAsync(Utf8.GetBytes(error.Message), LocalError, token).ConfigureAwait(false);
            }
        }
        await wire.WriteBlockAsync(ReadOnlyMemory<byte>.Empty, null, token).ConfigureAwait(false);
        await StatusAsync(errors.Count == 0 ? 0 : -13, errors.Count == 0 ? Empty : "Multi status", errors, token).ConfigureAwait(false);
    }
}
