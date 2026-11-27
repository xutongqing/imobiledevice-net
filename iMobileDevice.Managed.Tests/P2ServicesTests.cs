namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Text;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Services;
using iMobileDevice.Managed.Usbmuxd;
using Xunit;
using Xunit.Abstractions;

public class P2ServicesTests
{
    private readonly ITestOutputHelper output;

    public P2ServicesTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task DiagnosticsRelayParsesBatteryAndMobileGestaltAndRejectsFailureStatus()
    {
        byte[] okBattery = FramePlist(new PlistDictionary
        {
            ["Status"] = "Success",
            ["Diagnostics"] = new PlistDictionary
            {
                ["IORegistry"] = new PlistDictionary
                {
                    ["CurrentCapacity"] = 85L,
                    ["IsCharging"] = true,
                    ["ExternalConnected"] = true,
                    ["FullyCharged"] = false,
                    ["CycleCount"] = 312L,
                    ["DesignCapacity"] = 3110L,
                    ["NominalChargeCapacity"] = 2850L,
                    ["Temperature"] = 2940L,
                    ["Voltage"] = 4180L,
                    ["InstantAmperage"] = 950L,
                },
            },
        });

        byte[] failReply = FramePlist(new PlistDictionary
        {
            ["Status"] = "UnknownRequest",
        });

        using var stream = new ScriptedStream([.. okBattery, .. failReply]);
        await using var client = new DiagnosticsRelayClient(stream);

        BatteryInfo battery = await client.GetBatteryInfoAsync();
        Assert.Equal(85L, battery.CurrentCapacity);
        Assert.True(battery.IsCharging);
        Assert.Equal(312L, battery.CycleCount);
        Assert.Equal(29.4, battery.TemperatureCelsius!.Value, precision: 2);
        Assert.Equal(4180L, battery.VoltageMillivolts);

        var ex = await Assert.ThrowsAsync<DiagnosticsException>(() => client.QueryDiagnosticsAsync("Invalid"));
        Assert.Equal("UnknownRequest", ex.Status);
    }

    [Fact]
    public async Task SyslogRelayDecodesNullAndNewlineDelimitedLines()
    {
        byte[] raw = Encoding.UTF8.GetBytes("line-1\0line-2\r\nline-3\n\0tail-line");
        using var stream = new MemoryStream(raw, writable: false);
        await using var syslog = new SyslogRelayClient(stream);

        var lines = new List<string>();
        await foreach (string line in syslog.WatchLinesAsync())
        {
            lines.Add(line);
        }

        Assert.Equal(["line-1", "line-2", "line-3", "tail-line"], lines);
    }

    [Fact]
    public async Task NotificationProxySendsObserveAndStopsOnProxyDeath()
    {
        byte[] relay1 = FramePlist(new PlistDictionary
        {
            ["Command"] = "RelayNotification",
            ["Name"] = NotificationProxyNames.AppInstalled,
        });
        byte[] death = FramePlist(new PlistDictionary
        {
            ["Command"] = "ProxyDeath",
        });

        using var stream = new ScriptedStream([.. relay1, .. death]);
        await using var np = new NotificationProxyClient(stream);
        await np.ObserveAsync([NotificationProxyNames.AppInstalled, NotificationProxyNames.AppUninstalled]);

        var received = new List<string>();
        await foreach (string name in np.WatchAsync())
        {
            received.Add(name);
        }

        Assert.Equal([NotificationProxyNames.AppInstalled], received);
        Assert.True(stream.WrittenBytes.Length > 0);
    }

    [Fact]
    public async Task SpringBoardServicesParsesIconPngAndOrientation()
    {
        byte[] fakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] iconReply = FramePlist(new PlistDictionary { ["pngData"] = fakePng });
        byte[] orientReply = FramePlist(new PlistDictionary { ["interfaceOrientation"] = 1L });

        using var stream = new ScriptedStream([.. iconReply, .. orientReply]);
        await using var sb = new SpringBoardServicesClient(stream);

        Assert.Equal(fakePng, await sb.GetIconPngDataAsync("com.apple.Preferences"));
        Assert.Equal(SpringBoardInterfaceOrientation.Portrait, await sb.GetInterfaceOrientationAsync());
    }

    [UsbDeviceFact]
    public async Task P2ServicesReadOnlyE2EWorksOnRealDevice()
    {
        var usbmuxd = new UsbmuxdClient();
        await using LockdownClient lockdown = await LockdownClient.ConnectFirstDeviceAsync(usbmuxd, handshake: true);

        // 1. DiagnosticsRelay: Battery + MobileGestalt + Goodbye
        await using (var diag = await DiagnosticsRelayClient.ConnectAsync(lockdown))
        {
            BatteryInfo battery = await diag.GetBatteryInfoAsync();
            output.WriteLine($"[Diagnostics] Battery: {battery.CurrentCapacity}%, Charging={battery.IsCharging}, Temp={battery.TemperatureCelsius:F1}C, Voltage={battery.VoltageMillivolts}mV, Cycles={battery.CycleCount}");
            Assert.NotNull(battery.CurrentCapacity);

            PlistDictionary gestalt = await diag.QueryMobileGestaltAsync(["UserAssignedDeviceName", "ProductType"]);
            output.WriteLine($"[Diagnostics] MobileGestalt keys: {string.Join(", ", gestalt.Keys)}");
            Assert.NotEmpty(gestalt);

            await diag.GoodbyeAsync();
        }

        // 2. SyslogRelay: receive real live syslog lines
        await using (var syslog = await SyslogRelayClient.ConnectAsync(lockdown))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var lines = new List<string>();
            await foreach (string line in syslog.WatchLinesAsync(cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 3)
                {
                    break;
                }
            }

            output.WriteLine($"[Syslog] Received {lines.Count} live lines; sample: {lines[0][..Math.Min(120, lines[0].Length)]}");
            Assert.NotEmpty(lines);
        }

        // 3. NotificationProxy: observe standard notifications cleanly
        await using (var np = await NotificationProxyClient.ConnectAsync(lockdown))
        {
            await np.ObserveAsync([NotificationProxyNames.AppInstalled, NotificationProxyNames.TimezoneChanged]);
            await np.ShutdownAsync();
            output.WriteLine("[NotificationProxy] Observe + Shutdown succeeded.");
        }

        // 4. SpringBoardServices: fetch Settings app icon PNG & interface orientation & icon layout
        await using (var sb = await SpringBoardServicesClient.ConnectAsync(lockdown))
        {
            byte[] iconPng = await sb.GetIconPngDataAsync("com.apple.Preferences");
            Assert.True(iconPng.Length > 8);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, iconPng.AsSpan(0, 8).ToArray());

            SpringBoardInterfaceOrientation orient = await sb.GetInterfaceOrientationAsync();
            PlistArray iconState = await sb.GetIconStateAsync();
            output.WriteLine($"[SpringBoard] Settings icon PNG={iconPng.Length:N0} B, Orientation={orient}, IconState pages={iconState.Count}");
            Assert.NotEmpty(iconState);
        }

        // 5. CrashReports: list crash report directory (read-only)
        await using (var crashes = await CrashReportsClient.ConnectAsync(lockdown, flushPendingLogs: false))
        {
            var entries = await crashes.ListAsync("/", recursive: false);
            output.WriteLine($"[CrashReports] Root entries: {entries.Count}");
            Assert.NotNull(entries);
        }
    }

    private static byte[] FramePlist(PlistNode node)
    {
        byte[] body = PlistSerializer.WriteXml(node);
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)body.Length);
        body.CopyTo(frame.AsSpan(4));
        return frame;
    }

    private sealed class ScriptedStream(byte[] inbound) : Stream
    {
        private readonly MemoryStream reader = new(inbound, writable: false);
        private readonly MemoryStream writer = new();

        public byte[] WrittenBytes => writer.ToArray();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => reader.Length;
        public override long Position { get => reader.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => reader.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => reader.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => writer.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            writer.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
