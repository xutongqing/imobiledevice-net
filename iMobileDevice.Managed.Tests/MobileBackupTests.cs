namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using iMobileDevice.Managed.Backup;
using iMobileDevice.Managed.Plist;
using Xunit;

public sealed class MobileBackupTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("managed-backup-test-").FullName;

    [Fact]
    public async Task NegotiatesTwoVersionsAndRejectsAnUnsupportedVersion()
    {
        using var input = new MemoryStream();
        var send = new BackupWire(input);
        await send.SendAsync(new PlistArray { "DLMessageVersionExchange", 400, 0 }, default);
        await send.SendAsync(new PlistArray { "DLMessageDeviceReady" }, default);
        await send.SendAsync(Result(new() { ["ProtocolVersion"] = 2.1 }), default);
        using var stream = new ReplayStream(input.ToArray());
        await new DeviceLink(stream, root).HandshakeAsync(default);
        stream.Written.Position = 0;
        var output = new BackupWire(stream.Written);
        var versions = (await output.ReceiveAsync(default)).AsArray();
        Assert.Equal("DLVersionsOk", versions[1].AsString());
        Assert.Equal(400, versions[2].AsInt64());
        var hello = (await output.ReceiveAsync(default)).AsArray()[1].AsDictionary();
        Assert.Equal("Hello", hello.GetString("MessageName"));
        Assert.Equal(new[] { 2.0, 2.1 }, hello.GetArray("SupportedProtocolVersions")!.Select(v => v.AsReal()));
        input.SetLength(0);
        await send.SendAsync(new PlistArray { "DLMessageVersionExchange", 400, 0 }, default);
        await send.SendAsync(new PlistArray { "DLMessageDeviceReady" }, default);
        await send.SendAsync(Result(new() { ["ProtocolVersion"] = 9.0 }), default);
        using var unsupported = new ReplayStream(input.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => new DeviceLink(unsupported, root).HandshakeAsync(default));
    }

    [Fact]
    public async Task UploadMoveListCopyAndDownloadPreserveFileContentsAndReportIndividualErrors()
    {
        var token = CancellationToken.None;
        using var input = new MemoryStream();
        var send = new BackupWire(input);
        byte[] data = RandomNumberGenerator.GetBytes(97000);
        await send.SendAsync(new PlistArray { "DLMessageCreateDirectory", "phone/Snapshot" }, token);
        await send.SendAsync(new PlistArray { "DLMessageUploadFiles", new PlistArray(), 15.5 }, token);
        await send.WriteBlockAsync("device-file"u8.ToArray(), null, token);
        await send.WriteBlockAsync("phone/Snapshot/file"u8.ToArray(), null, token);
        await send.WriteBlockAsync(data, 12, token);
        await send.WriteBlockAsync(default, 0, token);
        await send.WriteBlockAsync("empty"u8.ToArray(), null, token);
        await send.WriteBlockAsync("phone/Snapshot/empty"u8.ToArray(), null, token);
        await send.WriteBlockAsync(default, 0, token);
        await send.WriteBlockAsync(default, null, token);
        await send.SendAsync(new PlistArray { "DLMessageMoveItems", new PlistDictionary { ["phone/Snapshot/file"] = "phone/ab/id" } }, token);
        await send.SendAsync(new PlistArray { "DLMessageCopyItem", "phone/ab", "phone/copy" }, token);
        await send.SendAsync(new PlistArray { "DLContentsOfDirectory", "phone/copy" }, token);
        await send.SendAsync(new PlistArray { "DLMessageDownloadFiles", new PlistArray { "phone/copy/id", "phone/missing" } }, token);
        await send.SendAsync(new PlistArray { "DLMessageRemoveItems", new PlistArray { "phone/copy" } }, token);
        await send.SendAsync(Result(new() { ["Content"] = "complete" }), token);
        using var duplex = new ReplayStream(input.ToArray());
        Assert.Equal("complete", (await new DeviceLink(duplex, root).RunAsync(null, token))!.AsString());
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(root, "phone/ab/id")));
        Assert.Equal(0, new FileInfo(Path.Combine(root, "phone/Snapshot/empty")).Length);
        Assert.False(Directory.Exists(Path.Combine(root, "phone/copy")));

        duplex.Written.Position = 0;
        var receive = new BackupWire(duplex.Written);
        for (int i = 0; i < 4; i++)
            Assert.Equal(0, (await receive.ReceiveAsync(token)).AsArray()[1].AsInt64());
        var listing = (await receive.ReceiveAsync(token)).AsArray()[3].AsDictionary();
        Assert.Equal(data.Length, listing["id"].AsDictionary().GetInt64("DLFileSize"));
        Assert.Equal("phone/copy/id", Encoding.UTF8.GetString(await ReadBlock(receive)));
        using var downloaded = new MemoryStream();
        while (true)
        {
            var frame = await ReadBlock(receive);
            if (frame[0] == 0) break;
            Assert.Equal(12, frame[0]);
            Assert.InRange(frame.Length, 2, 32769);
            downloaded.Write(frame.AsSpan(1));
        }
        Assert.Equal(data, downloaded.ToArray());
        Assert.Equal("phone/missing", Encoding.UTF8.GetString(await ReadBlock(receive)));
        Assert.Equal(6, (await ReadBlock(receive))[0]);
        Assert.Empty(await ReadBlock(receive));
        var multi = (await receive.ReceiveAsync(token)).AsArray();
        Assert.Equal(-13, multi[1].AsInt64());
        Assert.Equal(-6, multi[3].AsDictionary()["phone/missing"].AsDictionary().GetInt64("DLFileErrorCode"));
    }

    [Fact]
    public async Task OutOfSpaceAndInterruptedTransfersCannotReplaceFinishedBackup()
    {
        using (var store = new BackupStorage(root, "phone"))
        {
            MakeSnapshot(store.WorkingSnapshot);
            store.Publish();
        }
        using (var store = new BackupStorage(root, "phone"))
        {
            using var input = new MemoryStream();
            var send = new BackupWire(input);
            await send.SendAsync(new PlistArray { "DLMessageGetFreeDiskSpace" }, default);
            await send.SendAsync(new PlistArray { "DLMessagePurgeDiskSpace", 1 }, default);
            await send.SendAsync(Result(new(), 105), default);
            using var stream = new ReplayStream(input.ToArray());
            var ex = await Assert.ThrowsAsync<MobileBackupException>(() => new DeviceLink(stream, store.WorkRoot).RunAsync(null, default));
            Assert.Equal(105, ex.ErrorCode);
            stream.Written.Position = 0;
            var reply = new BackupWire(stream.Written);
            Assert.True((await reply.ReceiveAsync(default)).AsArray()[3].AsInt64() > 0);
            Assert.Equal(-1, (await reply.ReceiveAsync(default)).AsArray()[1].AsInt64());

            input.SetLength(0);
            await send.SendAsync(new PlistArray { "DLMessageUploadFiles" }, default);
            await send.WriteBlockAsync("name"u8.ToArray(), null, default);
            await send.WriteBlockAsync("phone/Manifest.db"u8.ToArray(), null, default);
            input.Write(new byte[] { 0, 0, 1, 0, 12, 1 });
            using var broken = new ReplayStream(input.ToArray());
            await Assert.ThrowsAsync<EndOfStreamException>(() => new DeviceLink(broken, store.WorkRoot).RunAsync(null, default));
            Assert.ThrowsAny<IOException>(() => store.Publish());
            Assert.ThrowsAny<IOException>(() => new BackupStorage(root, "phone"));
        }
        using var retry = new BackupStorage(root, "phone");
        Assert.False(Directory.Exists(retry.WorkingSnapshot));
        Assert.Equal("good", File.ReadAllText(Path.Combine(retry.Snapshot, "Manifest.db")));
        await BackupStorage.CopyAsync(retry.Snapshot, retry.WorkingSnapshot, default);
        File.WriteAllText(Path.Combine(retry.WorkingSnapshot, "Manifest.db"), "new");
        retry.Publish();
        Assert.Equal("new", File.ReadAllText(Path.Combine(retry.Snapshot, "Manifest.db")));
    }

    [Fact]
    public void EncryptedRestoreRequiresItsPasswordAndSelectsExplicitDevice()
    {
        var manifest = new PlistDictionary { ["IsEncrypted"] = true };
        Assert.Throws<ArgumentException>(() => MobileBackupClient.RestoreRequest("target", "source", manifest, new()));
        var request = MobileBackupClient.RestoreRequest("target", "source", manifest,
            new() { Password = "test-only", Reboot = false, RestoreSystemFiles = true });
        Assert.Equal("Restore", request.GetString("MessageName"));
        Assert.Equal("source", request.GetString("SourceIdentifier"));
        Assert.Equal("target", request.GetString("TargetIdentifier"));
        var options = request.GetDictionary("Options")!;
        Assert.Equal("test-only", options.GetString("Password"));
        Assert.False(options.GetBoolean("RestoreShouldReboot"));
        Assert.False(options.GetBoolean("RemoveItemsNotRestored"));
        Assert.True(options.GetBoolean("RestoreSystemFiles"));
    }

    [Fact]
    public void FailedPublicationKeepsCompletedSnapshotAndRetriesOnNextOpen()
    {
        using (var first = new BackupStorage(root, "phone"))
        {
            MakeSnapshot(first.WorkingSnapshot);
            // An occupied destination makes the rename fail without modifying that entry.
            File.WriteAllText(first.Snapshot, "occupied");
            Assert.ThrowsAny<IOException>(first.Publish);
        }
        Assert.Equal("occupied", File.ReadAllText(Path.Combine(root, "phone")));
        File.Delete(Path.Combine(root, "phone"));
        using var recovered = new BackupStorage(root, "phone");
        Assert.Equal("good", File.ReadAllText(Path.Combine(recovered.Snapshot, "Manifest.db")));
        Assert.False(Directory.Exists(recovered.WorkingSnapshot));
    }

    [Fact]
    public void PublishesWhileAnotherProcessHasAStagedFileOpenForReading()
    {
        using var store = new BackupStorage(root, "phone");
        MakeSnapshot(store.WorkingSnapshot);
        using var reader = new FileStream(Path.Combine(store.WorkingSnapshot, "Manifest.db"), FileMode.Open, FileAccess.Read, FileShare.Read);
        store.Publish();
        Assert.Equal("good", File.ReadAllText(Path.Combine(store.Snapshot, "Manifest.db")));
        BackupStorage.ValidateSnapshot(store.Snapshot);
    }

    [Fact]
    public void ResumesInterruptedFileByFilePublicationBeforeExposingFinishedStatus()
    {
        string state = Path.Combine(root, ".imobiledevice-managed", "phone");
        MakeSnapshot(Path.Combine(state, "work", "phone"));
        Directory.CreateDirectory(Path.Combine(root, "phone"));
        File.WriteAllText(Path.Combine(root, "phone", "Manifest.db"), "part");
        File.WriteAllText(Path.Combine(state, "copying"), "");
        File.WriteAllText(Path.Combine(state, "ready"), "");
        using var recovered = new BackupStorage(root, "phone");
        BackupStorage.ValidateSnapshot(recovered.Snapshot);
        Assert.Equal("good", File.ReadAllText(Path.Combine(recovered.Snapshot, "Manifest.db")));
        Assert.False(File.Exists(Path.Combine(state, "ready")));
    }

    [Fact]
    public async Task UnlocksManifestKeyAndListsAgainstAPrivateMetadataOnlyCopy()
    {
        using var bag = new MemoryStream();
        void Tag(string name, byte[] value)
        {
            bag.Write(Encoding.ASCII.GetBytes(name));
            byte[] size = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, value.Length);
            bag.Write(size);
            bag.Write(value);
        }
        Tag("DPSL", Enumerable.Repeat((byte)1, 20).ToArray());
        Tag("DPIC", [0, 0, 0, 40]);
        Tag("SALT", Enumerable.Repeat((byte)2, 20).ToArray());
        Tag("ITER", [0, 0, 0, 30]);
        Tag("CLAS", [0, 0, 0, 4]);
        Tag("WRAP", [0, 0, 0, 2]);
        Tag("WPKY", Convert.FromHexString("08bedabe8a44b15b4339b31ba2af0b9e90ab1c2bd0077a89ab2fee1306834213ac8ca54def8f2e1f"));
        var manifest = new PlistDictionary { ["BackupKeyBag"] = bag.ToArray(),
            ["ManifestKey"] = Convert.FromHexString("040000007110096028c88fef02a272bb688962295f2d4579069d7cbcb9e8bc5143bbe330ba69c0f968eaccb4") };
        Assert.Equal(Enumerable.Repeat((byte)4, 32), BackupManifest.UnlockManifestKey(manifest, "test-only"));
        Assert.Throws<CryptographicException>(() => BackupManifest.UnlockManifestKey(manifest, "incorrect"));
        manifest["IsEncrypted"] = true;
        using var store = new BackupStorage(root, "phone");
        MakeSnapshot(store.Snapshot);
        File.WriteAllBytes(Path.Combine(store.Snapshot, "Manifest.plist"), PlistSerializer.WriteBinary(manifest));
        File.WriteAllText(Path.Combine(store.Snapshot, "payload"), "omit from manifest listing");
        using var aes = Aes.Create();
        aes.Key = Enumerable.Repeat((byte)4, 32).ToArray();
        byte[] plain = RandomNumberGenerator.GetBytes(512);
        byte[] encrypted = aes.EncryptCbc(plain, new byte[16], PaddingMode.None);
        File.WriteAllBytes(Path.Combine(store.Snapshot, "Manifest.db"), encrypted);
        await BackupManifest.PrepareListAsync(store, "test-only", default);
        Assert.Equal(plain, File.ReadAllBytes(Path.Combine(store.WorkingSnapshot, "Manifest.db")));
        Assert.False(BackupStorage.ReadMetadata(store.WorkingSnapshot, "Manifest.plist").GetBoolean("IsEncrypted"));
        Assert.False(File.Exists(Path.Combine(store.WorkingSnapshot, "payload")));
        Assert.Equal(encrypted, File.ReadAllBytes(Path.Combine(store.Snapshot, "Manifest.db")));
    }

    [Fact]
    public async Task ExtractsHashedPayloadFromAnUnencryptedSnapshotWithoutADevice()
    {
        string folder = Path.Combine(root, "phone");
        MakeSnapshot(folder);
        byte[] data = RandomNumberGenerator.GetBytes(98305);
        string id = Convert.ToHexString(SHA1.HashData("HomeDomain-Library/file"u8)).ToLowerInvariant();
        Directory.CreateDirectory(Path.Combine(folder, id[..2]));
        File.WriteAllBytes(Path.Combine(folder, id[..2], id), data);
        using var output = new MemoryStream();
        await MobileBackupClient.ExtractFileAsync(root, "phone", "HomeDomain", "Library/file", output);
        Assert.Equal(data, output.ToArray());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("dir/file:stream")]
    [InlineData("/absolute")]
    [InlineData("dir/CON.txt")]
    public void DevicePathsStayInsideWorkingDirectory(string path) =>
        Assert.Throws<InvalidDataException>(() => BackupStorage.Resolve(root, path));

    internal static void MakeSnapshot(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Info.plist"), PlistSerializer.WriteXml(new PlistDictionary()));
        File.WriteAllBytes(Path.Combine(folder, "Status.plist"), PlistSerializer.WriteBinary(new PlistDictionary { ["SnapshotState"] = "finished" }));
        File.WriteAllBytes(Path.Combine(folder, "Manifest.plist"), PlistSerializer.WriteBinary(new PlistDictionary { ["IsEncrypted"] = false }));
        File.WriteAllText(Path.Combine(folder, "Manifest.db"), "good");
    }

    internal static PlistArray Result(PlistDictionary reply, int code = 0)
    {
        reply["ErrorCode"] = code;
        return new PlistArray { "DLMessageProcessMessage", reply };
    }
    internal static async Task<byte[]> ReadBlock(BackupWire wire) =>
        await wire.ReadBytesAsync(await wire.ReadLengthAsync(default), 100000, default);

    private sealed class ReplayStream(byte[] input) : MemoryStream(input)
    {
        public MemoryStream Written { get; } = new();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => Written.WriteAsync(buffer, ct);
    }
    public void Dispose() => Directory.Delete(root, true);
}
