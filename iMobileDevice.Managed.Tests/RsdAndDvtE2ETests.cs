namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using iMobileDevice.Managed.Dvt;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.RemoteXpc;
using iMobileDevice.Managed.Tunnel;
using iMobileDevice.Managed.Usbmuxd;
using Xunit;
using Xunit.Abstractions;

[Collection("USB device")]
public class RsdAndDvtE2ETests
{
    private readonly ITestOutputHelper output;

    public RsdAndDvtE2ETests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [UsbDeviceFact]
    public async Task PureManagedCoreDeviceTunnelRsdAndDvtScreenshotWorks()
    {
        var usbmuxd = new UsbmuxdClient();
        await using LockdownClient lockdown = await LockdownClient.ConnectFirstDeviceAsync(usbmuxd, handshake: true);

        await using CoreDeviceProxyClient proxy = await CoreDeviceProxyClient.ConnectAsync(lockdown);
        output.WriteLine($"[CoreDeviceProxy] Client={proxy.Parameters.ClientAddress}, Server={proxy.Parameters.ServerAddress}, RSDPort={proxy.Parameters.ServerRsdPort}, MTU={proxy.Parameters.Mtu}");

        await using var tunnel = new UserspaceTcpTunnel(proxy);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using RemoteServiceDiscoveryClient rsd = await RemoteServiceDiscoveryClient.ConnectAsync(tunnel, cts.Token);

        output.WriteLine($"[RSD] OSVersion={rsd.OsVersion}, ProductType={rsd.ProductType}, UDID={rsd.UniqueDeviceId}, Services={rsd.Services.Count}");
        Assert.True(rsd.Services.Count > 0);
        Assert.True(rsd.Services.ContainsKey(DvtClient.DtServiceHubName));

        await using DvtClient dvt = await DvtClient.ConnectAsync(rsd, cts.Token);
        byte[] png = await dvt.TakeScreenshotAsync(cts.Token);

        // Verify valid PNG magic header & IHDR dimensions
        byte[] pngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.True(png.Length > 24, $"Screenshot too small: {png.Length} bytes");
        Assert.Equal(pngMagic, png.AsSpan(0, 8).ToArray());

        uint width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        output.WriteLine($"[DVT Screenshot] Captured live PNG: {width}x{height} ({png.Length:N0} bytes)!");
        Assert.True(width > 0 && height > 0);

        string? outPath = Environment.GetEnvironmentVariable("IMOBILEDEVICE_SCREENSHOT_OUT");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            await File.WriteAllBytesAsync(outPath, png, cts.Token);
            output.WriteLine($"[DVT Screenshot] Saved to {outPath}");
        }
    }
}
