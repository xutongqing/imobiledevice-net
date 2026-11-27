namespace iMobileDevice.Managed.Tests;

using System.Diagnostics;
using iMobileDevice.Managed.Dvt;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.RemoteXpc;
using iMobileDevice.Managed.Tunnel;
using iMobileDevice.Managed.Usbmuxd;
using Xunit;
using Xunit.Abstractions;

[Collection("USB device")]
public class DvtProcessMonitorE2ETests(ITestOutputHelper output)
{
    private static async Task<UsbmuxdDeviceInfo> SelectDeviceAsync(UsbmuxdClient mux, CancellationToken token)
    {
        var usb = (await mux.ListDevicesAsync(token)).Where(d => d.ConnectionType == UsbmuxdConnectionType.Usb);
        var udid = Environment.GetEnvironmentVariable("IMOBILEDEVICE_UDID");
        return udid == null ? usb.Single() : usb.Single(d => string.Equals(d.SerialNumber, udid, StringComparison.OrdinalIgnoreCase));
    }

    [UsbDeviceFact]
    public async Task SelectListSampleAndCancel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var device = await SelectDeviceAsync(new UsbmuxdClient(), timeout.Token);
        await Assert.ThrowsAsync<LockdownException>(() => DvtClient.ConnectUsbAsync("absent-test-device", timeout.Token));
        await using var client = await DvtClient.ConnectUsbAsync(device.SerialNumber, timeout.Token);
        var processes = await client.ListProcessesAsync(timeout.Token);
        Assert.Contains(processes, p => p.ProcessId > 0 && !string.IsNullOrEmpty(p.Name));
        output.WriteLine($"Listed {processes.Count} running processes.");

        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var samples = client.WatchProcessesAsync(500, sampling.Token).GetAsyncEnumerator();
        for (int i = 1; i <= 3; i++)
        {
            Assert.True(await samples.MoveNextAsync());
            var batch = samples.Current;
            Assert.Contains(batch, p => p.ProcessId > 0 && p.CpuPercent >= 0 && p.PhysicalFootprintBytes > 0);
            output.WriteLine($"Batch {i}: {batch.Count} process measurements.");
        }
        Assert.NotEmpty(await client.ListProcessesAsync(timeout.Token));
        sampling.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => samples.MoveNextAsync().AsTask());
        Assert.NotEmpty(await client.ListProcessesAsync(timeout.Token));
    }

    [UsbDeviceFact]
    public async Task MonitorApplicationByBundleIdentifier()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var device = await SelectDeviceAsync(new UsbmuxdClient(), timeout.Token);
        await using var client = await DvtClient.ConnectUsbAsync(device.SerialNumber, timeout.Token);
        const string springBoard = "com.apple.springboard";
        int? pid = await client.GetApplicationProcessIdAsync(springBoard, timeout.Token);
        Assert.True(pid > 0);
        var running = await client.ListProcessesAsync(timeout.Token);
        Assert.Contains(running, p => p.ProcessId == pid && p.Name == "SpringBoard");
        Assert.Contains(running, p => !string.IsNullOrEmpty(p.BundleIdentifier));

        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var samples = client.WatchApplicationAsync(springBoard, 500, sampling.Token).GetAsyncEnumerator();
        int measured = 0;
        for (int i = 0; i < 6; i++)
        {
            Assert.True(await samples.MoveNextAsync());
            Assert.Equal(pid, samples.Current.ProcessId);
            if (samples.Current.Measurement is { } row)
            {
                Assert.Equal(pid, row.ProcessId);
                Assert.Equal("SpringBoard", row.Name);
                if (row is { CpuPercent: >= 0, PhysicalFootprintBytes: > 0 })
                {
                    measured++;
                }
            }
        }
        Assert.True(measured > 0);
        output.WriteLine($"Received CPU and physical footprint for SpringBoard in {measured}/6 batches.");
        sampling.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => samples.MoveNextAsync().AsTask());

        string absentBundle = $"org.example.imobiledevice.absent-{Guid.NewGuid():N}";
        Assert.Null(await client.GetApplicationProcessIdAsync(absentBundle, timeout.Token));
        await using (var absent = client.WatchApplicationAsync(absentBundle, 500, timeout.Token).GetAsyncEnumerator())
        {
            Assert.True(await absent.MoveNextAsync());
            Assert.Null(absent.Current.ProcessId);
            Assert.Null(absent.Current.Measurement);
        }
        Assert.Equal(pid, await client.GetApplicationProcessIdAsync(springBoard, timeout.Token));
    }

    [UsbDeviceFact]
    public async Task LosingTheUsbTransportEndsSampling()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var mux = new UsbmuxdClient();
        var device = await SelectDeviceAsync(mux, timeout.Token);
        await using var lockdown = await LockdownClient.ConnectAsync(mux, device, cancellationToken: timeout.Token);
        await using var proxy = await CoreDeviceProxyClient.ConnectAsync(lockdown, timeout.Token);
        await using var tunnel = new UserspaceTcpTunnel(proxy);
        await using var rsd = await RemoteServiceDiscoveryClient.ConnectAsync(tunnel, timeout.Token);
        await using var client = await DvtClient.ConnectAsync(rsd, timeout.Token);
        await using var samples = client.WatchProcessesAsync(500, timeout.Token).GetAsyncEnumerator();
        Assert.True(await samples.MoveNextAsync());

        async Task DrainAsync()
        {
            while (await samples.MoveNextAsync()) { }
        }

        var reading = DrainAsync();
        var elapsed = Stopwatch.StartNew();
        await proxy.DisposeAsync();
        var error = await Record.ExceptionAsync(() => reading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error is IOException or ObjectDisposedException, $"Expected a transport failure, got {error?.GetType().Name ?? "normal completion"}.");
        output.WriteLine($"Sampling ended {elapsed.ElapsedMilliseconds} ms after closing the USB transport ({error!.GetType().Name}).");
    }
}
