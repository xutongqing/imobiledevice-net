namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using iMobileDevice.Managed.Diagnostics;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Usbmuxd;
using Xunit;

public class DeviceDiscoveryTests
{
    private static PlistDictionary Route(int id, string serial, string type) => new()
    {
        ["Properties"] = new PlistDictionary { ["DeviceID"] = id, ["SerialNumber"] = serial, ["ConnectionType"] = type },
    };

    [Fact]
    public async Task SelectsOnlyMatchingRouteAndConnectsToItsDeviceId()
    {
        var devices = new PlistArray { Route(1, "AB-CD", "Network"), Route(2, "DIFFERENT", "USB"), Route(3, "AB-CD", "USB") };
        await WithDaemonAsync(async (listener, ct) =>
        {
            for (int i = 0; i < 5; i++) await ListAsync(listener, devices, ct);
            using var socket = await listener.AcceptTcpClientAsync(ct);
            var connect = await ExchangeAsync(socket, true, "Connect", Result(0), ct);
            Assert.Equal(3L, connect.GetInt64("DeviceID"));
            await ExchangeAsync(socket, false, "QueryType", new() { ["Type"] = "com.apple.mobile.lockdown" }, ct);
        }, async (mux, ct) =>
        {
            Assert.Equal(new[] { 2, 3 }, (await mux.ListDevicesByConnectionTypeAsync(UsbmuxdConnectionType.Usb, ct)).Select(d => d.DeviceId));
            Assert.Equal(3, (await mux.SelectDeviceAsync(" abcd ", cancellationToken: ct))?.DeviceId);
            Assert.Equal(1, (await mux.SelectDeviceAsync("ab-cd", UsbmuxdConnectionType.Network, ct))?.DeviceId);
            var ex = await Assert.ThrowsAsync<LockdownException>(() => LockdownClient.ConnectByUdidAsync("absent", mux, cancellationToken: ct));
            Assert.Equal("DeviceNotFound", ex.ErrorCode);
            await using var client = await LockdownClient.ConnectByUdidAsync("abcd", mux, handshake: false, cancellationToken: ct);
            Assert.Equal("com.apple.mobile.lockdown", await client.QueryTypeAsync(ct));
        });
    }

    [Fact]
    public async Task DiagnosticRetainsFailedRoutesAndDoesNotPairAnUntrustedDevice()
    {
        var devices = new PlistArray { Route(4, "GONE", "USB"), Route(5, "LOCKED", "Network"), Route(6, "NEW", "USB") };
        await WithDaemonAsync(async (listener, ct) =>
        {
            await ListAsync(listener, devices, ct);
            using (var gone = await listener.AcceptTcpClientAsync(ct))
                await ExchangeAsync(gone, true, "Connect", Result(2), ct);
            using (var locked = await listener.AcceptTcpClientAsync(ct))
            {
                await ExchangeAsync(locked, true, "Connect", Result(0), ct);
                await ExchangeAsync(locked, false, "QueryType", new() { ["Error"] = "PasswordProtected" }, ct);
            }
            using var device = await listener.AcceptTcpClientAsync(ct);
            await ExchangeAsync(device, true, "Connect", Result(0), ct);
            await ExchangeAsync(device, false, "QueryType", new() { ["Type"] = "com.apple.mobile.lockdown" }, ct);
            using (var db = await listener.AcceptTcpClientAsync(ct))
                await ExchangeAsync(db, true, "ReadPairRecord", Result(2), ct);
            await ExchangeAsync(device, false, "GetValue", new() { ["Value"] = new PlistDictionary { ["ProductType"] = "example" } }, ct);
        }, async (mux, ct) =>
        {
            var report = await DeviceConnectionDiagnostics.InspectAsync(mux, cancellationToken: ct);
            Assert.Equal(DiscoveryStatus.DevicesFound, report.Status);
            Assert.Equal(new[] { DeviceConnectionStatus.Disconnected, DeviceConnectionStatus.DeviceLocked,
                DeviceConnectionStatus.PairingRequired }, report.Devices.Select(d => d.Status));
            Assert.Equal("PasswordProtected", report.Devices[1].ErrorCode);
            Assert.Equal("example", report.Devices[2].Summary?.ProductType);
            Assert.False(report.Devices[2].IsSslEnabled);
        });
    }

    [Fact]
    public async Task DiagnosticDistinguishesEmptyListMissingTargetAndUnavailableService()
    {
        await WithDaemonAsync(async (listener, ct) =>
        {
            await ListAsync(listener, new(), ct);
            await ListAsync(listener, new() { Route(1, "connected", "USB") }, ct);
        }, async (mux, ct) =>
        {
            Assert.Equal(DiscoveryStatus.NoDevices, (await DeviceConnectionDiagnostics.InspectAsync(mux, cancellationToken: ct)).Status);
            var missing = await DeviceConnectionDiagnostics.InspectAsync(mux, "absent", cancellationToken: ct);
            Assert.Equal(DiscoveryStatus.NoMatch, missing.Status);
            Assert.Empty(missing.Devices);
        });

        // A missing socket is unavailable immediately. A bound TCP port can instead time out.
        var missingSocket = new UnixDomainSocketEndPoint($"missing-{Guid.NewGuid():N}.sock");
        var unavailable = await DeviceConnectionDiagnostics.InspectAsync(new UsbmuxdClient(missingSocket));
        Assert.Equal(DiscoveryStatus.DaemonUnavailable, unavailable.Status);
        Assert.Equal(missingSocket.ToString(), unavailable.Endpoint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AcceptedPairRecordSurvivesCancellationOrStoreFailure(bool autoPair, bool failSave)
    {
        var device = new UsbmuxdDeviceInfo(7, "test-device", 1, UsbmuxdConnectionType.Usb);
        using var deviceKey = RSA.Create(2048);
        byte[] publicKey = Encoding.ASCII.GetBytes(deviceKey.ExportRSAPublicKeyPem());
        byte[] escrow = [1, 2, 3];
        using var caller = new CancellationTokenSource();
        await WithDaemonAsync(async (listener, ct) =>
        {
            using var peer = await listener.AcceptTcpClientAsync(ct);
            await ExchangeAsync(peer, true, "Connect", Result(0), ct);
            if (autoPair)
            {
                await ExchangeAsync(peer, false, "QueryType", new() { ["Type"] = "com.apple.mobile.lockdown" }, ct);
                using var lookup = await listener.AcceptTcpClientAsync(ct);
                await ExchangeAsync(lookup, true, "ReadPairRecord", Result(2), ct);
            }
            await ExchangeAsync(peer, false, "GetValue", new() { ["Value"] = publicKey }, ct);
            await ExchangeAsync(peer, false, "GetValue", new(), ct);
            using (var buid = await listener.AcceptTcpClientAsync(ct))
                await ExchangeAsync(buid, true, "ReadBUID", new() { ["BUID"] = "test-buid" }, ct);
            var pair = await ExchangeAsync(peer, false, "Pair", new() { ["EscrowBag"] = escrow }, ct);
            using var db = await listener.AcceptTcpClientAsync(ct);
            var save = await ExchangeAsync(db, true, "SavePairRecord", Result(failSave ? 1 : 0), ct, caller.Cancel);
            byte[] stored = save.GetData("PairRecordData")!;
            var record = PairRecord.FromPlist(PlistSerializer.Parse(stored).AsDictionary());
            Assert.Equal(pair["PairRecord"].AsDictionary().GetString("HostID"), record.HostId);
            Assert.Equal(escrow, record.EscrowBag);
            Assert.NotEmpty(record.HostPrivateKey);
            if (failSave)
            {
                using var retry = await listener.AcceptTcpClientAsync(ct);
                var saved = await ExchangeAsync(retry, true, "SavePairRecord", Result(0), ct);
                Assert.Equal(stored, saved.GetData("PairRecordData"));
            }
        }, async (mux, ct) =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller.Token, ct);
            PairRecordSaveException error;
            if (autoPair)
            {
                error = await Assert.ThrowsAsync<PairRecordSaveException>(() =>
                    LockdownClient.ConnectAsync(mux, device, autoPair: true, cancellationToken: lifetime.Token));
            }
            else
            {
                await using var lockdown = await LockdownClient.ConnectAsync(mux, device, handshake: false, cancellationToken: ct);
                if (!failSave)
                {
                    var record = await lockdown.PairAsync(cancellationToken: lifetime.Token);
                    Assert.Same(record, lockdown.PairRecord);
                    Assert.True(caller.IsCancellationRequested);
                    return;
                }
                error = await Assert.ThrowsAsync<PairRecordSaveException>(() => lockdown.PairAsync(cancellationToken: lifetime.Token));
                Assert.Same(error.PairRecord, lockdown.PairRecord);
            }
            Assert.IsType<UsbmuxdException>(error.InnerException);
            Assert.Equal(device, error.Device);
            await mux.SavePairRecordAsync(error.Device.SerialNumber, error.PairRecord, error.Device.DeviceId, ct);
        });
    }

    [Theory]
    [InlineData("InvalidHostID", DeviceConnectionStatus.PairingRejected)]
    [InlineData("PasswordProtected", DeviceConnectionStatus.DeviceLocked)]
    [InlineData(null, DeviceConnectionStatus.Connected)]
    public async Task DiagnosticChecksThePairRecordWithTheDevice(string? error, DeviceConnectionStatus expected)
    {
        await WithDaemonAsync(async (listener, ct) =>
        {
            await ListAsync(listener, new() { Route(7, "AA-BB", "USB") }, ct);
            using var device = await listener.AcceptTcpClientAsync(ct);
            await ExchangeAsync(device, true, "Connect", Result(0), ct);
            await ExchangeAsync(device, false, "QueryType", new() { ["Type"] = "com.apple.mobile.lockdown" }, ct);
            // Only record presence and StartSession are under test. This peer does not request TLS.
            var record = new PlistDictionary { ["HostID"] = "host", ["SystemBUID"] = "buid" };
            foreach (string key in new[] { "DeviceCertificate", "HostCertificate", "HostPrivateKey", "RootCertificate", "RootPrivateKey" })
                record[key] = Array.Empty<byte>();
            using (var db = await listener.AcceptTcpClientAsync(ct))
                await ExchangeAsync(db, true, "ReadPairRecord", new() { ["PairRecordData"] = PlistSerializer.WriteXml(record) }, ct);
            if (error != null)
            {
                await ExchangeAsync(device, false, "StartSession", new() { ["Error"] = error }, ct);
                return;
            }
            await ExchangeAsync(device, false, "StartSession", new() { ["SessionID"] = "session" }, ct);
            await ExchangeAsync(device, false, "GetValue", new() { ["Value"] = new PlistDictionary { ["DeviceName"] = "test" } }, ct);
            await ExchangeAsync(device, false, "StopSession", new(), ct);
        }, async (mux, ct) =>
        {
            var report = await DeviceConnectionDiagnostics.InspectAsync(mux, "aabb", cancellationToken: ct);
            var result = Assert.Single(report.Devices);
            Assert.Equal(expected, result.Status);
            Assert.Equal(error, result.ErrorCode);
            Assert.False(result.IsSslEnabled);
            if (error == null) Assert.Equal("test", result.Summary?.DeviceName);
        });
    }

    [Fact]
    public async Task ProbeTimeoutIsReportedButCallerCancellationPropagates()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var mux = new UsbmuxdClient(silent.LocalEndpoint);
        var result = await DeviceConnectionDiagnostics.InspectAsync(mux, timeout: TimeSpan.FromMilliseconds(250));
        Assert.Equal(DiscoveryStatus.TimedOut, result.Status);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeviceConnectionDiagnostics.InspectAsync(mux, cancellationToken: cancel.Token));
    }

    private static async Task WithDaemonAsync(Func<TcpListener, CancellationToken, Task> serve, Func<UsbmuxdClient, CancellationToken, Task> run)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await Task.WhenAll(serve(listener, limit.Token), run(new UsbmuxdClient(listener.LocalEndpoint), limit.Token));
    }

    private static async Task ListAsync(TcpListener listener, PlistArray devices, CancellationToken ct)
    {
        using var socket = await listener.AcceptTcpClientAsync(ct);
        await ExchangeAsync(socket, true, "ListDevices", new() { ["DeviceList"] = devices }, ct);
    }

    private static PlistDictionary Result(int number) => new() { ["MessageType"] = "Result", ["Number"] = number };

    private static async Task<PlistDictionary> ExchangeAsync(TcpClient socket, bool mux, string expected, PlistDictionary reply, CancellationToken ct, Action? beforeReply = null)
    {
        var stream = socket.GetStream();
        var header = new byte[mux ? 16 : 4];
        await stream.ReadExactlyAsync(header, ct);
        int size = mux ? BinaryPrimitives.ReadInt32LittleEndian(header) - 16 : BinaryPrimitives.ReadInt32BigEndian(header);
        Assert.InRange(size, 1, 1024 * 1024);
        var body = new byte[size];
        await stream.ReadExactlyAsync(body, ct);
        var request = PlistSerializer.Parse(body).AsDictionary();
        Assert.Equal(expected, request.GetString(mux ? "MessageType" : "Request"));
        beforeReply?.Invoke();
        body = PlistSerializer.WriteXml(reply);
        if (mux) BinaryPrimitives.WriteInt32LittleEndian(header, body.Length + 16);
        else BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        return request;
    }
}
