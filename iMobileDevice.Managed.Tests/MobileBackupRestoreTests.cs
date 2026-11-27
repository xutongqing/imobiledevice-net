namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Backup;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Usbmuxd;
using Xunit;

public class MobileBackupRestoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(205)]
    public async Task RestoreServesBackupToPairedTargetAndPreservesTheOriginal(int error)
    {
        var root = Directory.CreateTempSubdirectory("managed-restore-test-");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var token = deadline.Token;
        var mux = new UsbmuxdClient(listener.LocalEndpoint);
        var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
        MobileBackupTests.MakeSnapshot(source.FullName);
        byte[] expected = "original backup payload"u8.ToArray();
        File.WriteAllBytes(Path.Combine(source.FullName, "file"), expected);
        var record = new PairRecord { HostId = "host", SystemBuid = "buid", HostPrivateKey = [], HostCertificate = [],
            RootPrivateKey = [], RootCertificate = [], DeviceCertificate = [], EscrowBag = [8, 3] };
        async Task Client()
        {
            await using var lockdown = await LockdownClient.ConnectAsync(mux, new(1, "target", 1, UsbmuxdConnectionType.Usb),
                pairRecord: record, cancellationToken: token);
            var client = new MobileBackupClient(lockdown);
            Task restore = client.RestoreAsync(root.FullName, new() { SourceIdentifier = "source", Reboot = false }, cancellationToken: token);
            if (error == 0) await restore;
            else Assert.Equal(error, (await Assert.ThrowsAsync<MobileBackupException>(() => restore)).ErrorCode);
        }
        async Task Device()
        {
            using var control = await AcceptMuxAsync(listener, token);
            var lockdown = new BackupWire(control.GetStream());
            await LockdownReply("QueryType", new() { ["Type"] = "com.apple.mobile.lockdown" });
            await LockdownReply("StartSession", new() { ["SessionID"] = "session" });
            var service = await LockdownReply("StartService", new() { ["Port"] = 1001 });
            Assert.Equal(MobileBackupClient.ServiceName, service.GetString("Service"));
            Assert.Equal(record.EscrowBag, service.GetData("EscrowBag"));
            using var backup = await AcceptMuxAsync(listener, token);
            var link = new BackupWire(backup.GetStream());
            await link.SendAsync(new PlistArray { "DLMessageVersionExchange", 400, 0 }, token);
            Assert.Equal("DLVersionsOk", (await link.ReceiveAsync(token)).AsArray()[1].AsString());
            await link.SendAsync(new PlistArray { "DLMessageDeviceReady" }, token);
            Assert.Equal("Hello", (await link.ReceiveAsync(token)).AsArray()[1].AsDictionary().GetString("MessageName"));
            await link.SendAsync(MobileBackupTests.Result(new() { ["ProtocolVersion"] = 2.1 }), token);

            Assert.Equal(AfcClient.ServiceName, (await LockdownReply("StartService", new() { ["Port"] = 1002 })).GetString("Service"));
            using var afc = await AcceptMuxAsync(listener, token);
            Assert.Equal("com.apple.mobile.notification_proxy", (await LockdownReply("StartService", new() { ["Port"] = 1003 })).GetString("Service"));
            using var notifications = await AcceptMuxAsync(listener, token);
            var proxy = new BackupWire(notifications.GetStream());
            await Notification("syncWillStart");
            await AfcReplyAsync(afc.GetStream(), AfcOperation.FileRefOpen, AfcOperation.FileRefOpenResult, 1, token);
            await Notification("syncLockRequest");
            await AfcReplyAsync(afc.GetStream(), AfcOperation.FileRefLock, AfcOperation.Status, 0, token);
            await Notification("syncDidStart");
            var restore = (await link.ReceiveAsync(token)).AsArray()[1].AsDictionary();
            Assert.Equal("Restore", restore.GetString("MessageName"));
            Assert.Equal("source", restore.GetString("SourceIdentifier"));
            Assert.Equal("target", restore.GetString("TargetIdentifier"));
            Assert.False(restore.GetDictionary("Options")!.GetBoolean("RestoreShouldReboot"));
            await AfcReplyAsync(afc.GetStream(), AfcOperation.RemovePath, AfcOperation.Status, (ulong)AfcErrorCode.ObjectNotFound, token);

            await link.SendAsync(new PlistArray { "DLMessageDownloadFiles", new PlistArray { "source/file" } }, token);
            Assert.Equal("source/file"u8.ToArray(), await MobileBackupTests.ReadBlock(link));
            Assert.Equal(new byte[] { 12 }.Concat(expected), await MobileBackupTests.ReadBlock(link));
            Assert.Equal(new byte[] { 0 }, await MobileBackupTests.ReadBlock(link));
            Assert.Empty(await MobileBackupTests.ReadBlock(link));
            Assert.Equal(0, (await link.ReceiveAsync(token)).AsArray()[1].AsInt64());
            await link.SendAsync(new PlistArray { "DLMessageRemoveItems", new PlistArray { "source/file" } }, token);
            Assert.Equal(0, (await link.ReceiveAsync(token)).AsArray()[1].AsInt64());
            await link.SendAsync(MobileBackupTests.Result(new(), error), token);
            if (error == 0) Assert.Equal("DLMessageDisconnect", (await link.ReceiveAsync(token)).AsArray()[0].AsString());

            await AfcReplyAsync(afc.GetStream(), AfcOperation.FileRefLock, AfcOperation.Status, 0, token);
            await AfcReplyAsync(afc.GetStream(), AfcOperation.FileRefClose, AfcOperation.Status, 0, token);
            await Notification("syncDidFinish");
            await LockdownReply("StopSession", new());

            async Task<PlistDictionary> LockdownReply(string type, PlistDictionary reply)
            {
                var request = (await lockdown.ReceiveAsync(token)).AsDictionary();
                Assert.Equal(type, request.GetString("Request"));
                await lockdown.SendAsync(reply, token);
                return request;
            }
            async Task Notification(string suffix)
            {
                var msg = (await proxy.ReceiveAsync(token)).AsDictionary();
                Assert.Equal("PostNotification", msg.GetString("Command"));
                Assert.Equal("com.apple.itunes-mobdev." + suffix, msg.GetString("Name"));
            }
        }

        try
        {
            await Task.WhenAll(Device(), Client()).WaitAsync(token);
            Assert.Equal(expected, File.ReadAllBytes(Path.Combine(source.FullName, "file")));
        }
        finally { root.Delete(true); }
    }

    private static async Task<TcpClient> AcceptMuxAsync(TcpListener listener, CancellationToken token)
    {
        var client = await listener.AcceptTcpClientAsync(token);
        var stream = client.GetStream();
        var header = new byte[16];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.InRange(length, 17, 65536);
        var payload = new byte[length - 16];
        await stream.ReadExactlyAsync(payload, token);
        Assert.Equal("Connect", PlistSerializer.Parse(payload).AsDictionary().GetString("MessageType"));
        payload = PlistSerializer.WriteXml(new PlistDictionary { ["MessageType"] = "Result", ["Number"] = 0 });
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length + 16);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
        return client;
    }

    private static async Task AfcReplyAsync(Stream stream, AfcOperation expected, AfcOperation reply, ulong value, CancellationToken token)
    {
        var header = new byte[40];
        await stream.ReadExactlyAsync(header, token);
        int length = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)));
        Assert.InRange(length, 40, 65536);
        Assert.Equal(expected, (AfcOperation)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32)));
        await stream.ReadExactlyAsync(new byte[length - 40], token);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), 48);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), 48);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), (ulong)reply);
        await stream.WriteAsync(header, token);
        var result = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(result, value);
        await stream.WriteAsync(result, token);
    }
}
