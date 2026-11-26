namespace iMobileDevice.Managed.Tests;

using System.Security.Cryptography;
using System.Text;
using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Usbmuxd;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Real-device end-to-end tests for pure managed C# protocols (zero native libimobiledevice/libusbmuxd/libplist usage).
/// Enabled when IMOBILEDEVICE_RUN_DEVICE_E2E=1.
/// </summary>
[Collection("USB device")]
public class ManagedDeviceE2ETests
{
    private readonly ITestOutputHelper output;

    public ManagedDeviceE2ETests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [UsbDeviceFact]
    public async Task PureManagedUsbDiscoveryLockdownTlsAndAfcRoundTripWorks()
    {
        var usbmuxd = new UsbmuxdClient();

        // 1. Discover USB devices via pure C# usbmuxd
        IReadOnlyList<UsbmuxdDeviceInfo> devices = await usbmuxd.ListDevicesAsync();
        Assert.NotEmpty(devices);
        UsbmuxdDeviceInfo device = devices.First(d => d.ConnectionType == UsbmuxdConnectionType.Usb);
        output.WriteLine($"[Usbmuxd] Discovered USB device: ID={device.DeviceId}, UDID={device.SerialNumber}, PID=0x{device.ProductId:X4}");

        // 2. Verify live WatchDevicesAsync receives initial Attached event & honors CancellationToken
        using (var watchCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            UsbmuxdDeviceEvent? firstEvent = null;
            await foreach (UsbmuxdDeviceEvent ev in usbmuxd.WatchDevicesAsync(watchCts.Token))
            {
                firstEvent = ev;
                break;
            }

            Assert.NotNull(firstEvent);
            Assert.Equal(UsbmuxdEventKind.Attached, firstEvent!.Kind);
            Assert.Equal(device.SerialNumber, firstEvent.Device?.SerialNumber);
            output.WriteLine($"[Usbmuxd] WatchDevicesAsync received Attached for {firstEvent.Device?.SerialNumber}");
        }

        // 3. Read BUID and PairRecord directly from usbmuxd (pure C#), plus verify missing record returns null
        string buid = await usbmuxd.ReadBuidAsync();
        PairRecord? pairRecord = await usbmuxd.ReadPairRecordAsync(device.SerialNumber);
        PairRecord? missingRecord = await usbmuxd.ReadPairRecordAsync("00008020-0000000000000000");
        Assert.False(string.IsNullOrWhiteSpace(buid));
        Assert.NotNull(pairRecord);
        Assert.Null(missingRecord);
        output.WriteLine($"[Usbmuxd] BUID={buid}, HostID={pairRecord!.HostId}, MissingRecord=null");

        // 4. Establish Lockdown session + pinned peer-cert TLS handshake + concurrent GetValueAsync + Stop/Restart session
        await using LockdownClient lockdown = await LockdownClient.ConnectAsync(usbmuxd, device, handshake: true);
        Assert.True(lockdown.IsSslEnabled);
        Assert.False(string.IsNullOrWhiteSpace(lockdown.SessionId));

        // Concurrent queries over the same LockdownClient verify serialized framing
        Task<string?>[] parallelQueries =
        {
            lockdown.GetStringValueAsync("DeviceName"),
            lockdown.GetStringValueAsync("ProductType"),
            lockdown.GetStringValueAsync("ProductVersion"),
            lockdown.GetStringValueAsync("BuildVersion"),
        };
        string?[] values = await Task.WhenAll(parallelQueries);
        string? deviceName = values[0];
        string? productType = values[1];
        string? productVersion = values[2];
        string? buildVersion = values[3];

        output.WriteLine($"[Lockdown TLS] DeviceName={deviceName}, ProductType={productType}, iOS={productVersion} ({buildVersion}), SessionID={lockdown.SessionId}");
        Assert.False(string.IsNullOrWhiteSpace(deviceName));
        Assert.False(string.IsNullOrWhiteSpace(productType));
        Assert.False(string.IsNullOrWhiteSpace(productVersion));

        // Verify clean StopSession downgrades SSL and allows restarting session on the same connection
        await lockdown.StopSessionAsync();
        Assert.False(lockdown.IsSslEnabled);
        Assert.Null(lockdown.SessionId);
        await lockdown.StartSessionAsync(pairRecord);
        Assert.True(lockdown.IsSslEnabled);
        Assert.Equal(deviceName, await lockdown.GetStringValueAsync("DeviceName"));
        output.WriteLine($"[Lockdown TLS] StopSession -> StartSession restart succeeded (New SessionID={lockdown.SessionId})");

        // 5. Connect to AFC via pure C# Lockdown StartService + Usbmuxd Connect
        await using AfcClient afc = await AfcClient.ConnectAsync(lockdown);
        IReadOnlyDictionary<string, string> afcInfo = await afc.GetDeviceInfoAsync();
        Assert.True(afcInfo.ContainsKey("Model"));
        output.WriteLine($"[AFC] DeviceInfo Model={afcInfo["Model"]}, FSTotalBytes={afcInfo.GetValueOrDefault("FSTotalBytes")}");

        IReadOnlyList<string> rootEntries = await afc.ReadDirectoryAsync("/");
        Assert.Contains("DCIM", rootEntries);
        output.WriteLine($"[AFC] '/' entries ({rootEntries.Count}): {string.Join(", ", rootEntries)}");

        // 6. Full file read/write/seek/tell/rename/delete round-trip (empty 0-byte file + >1.5 MiB multi-chunk file)
        string testDir = $"/iMobileDeviceManagedTest_{Guid.NewGuid():N}";
        string emptyFile = $"{testDir}/empty.bin";
        string testFile1 = $"{testDir}/large.bin";
        string testFile2 = $"{testDir}/renamed.bin";

        await afc.MakeDirectoryAsync(testDir);
        try
        {
            // Empty 0-byte file test
            await afc.WriteAllBytesAsync(emptyFile, ReadOnlyMemory<byte>.Empty);
            AfcFileInfo emptyInfo = await afc.GetFileInfoAsync(emptyFile);
            Assert.Equal(0, emptyInfo.Size);
            Assert.Empty(await afc.ReadAllBytesAsync(emptyFile));

            // >1.5 MiB file to exercise multi-chunk write & multi-chunk read
            byte[] payload = RandomNumberGenerator.GetBytes(1_572_864 + 777);
            await afc.WriteAllBytesAsync(testFile1, payload);

            AfcFileInfo info = await afc.GetFileInfoAsync(testFile1);
            Assert.True(info.IsRegularFile);
            Assert.Equal(payload.Length, info.Size);

            byte[] readBack = await afc.ReadAllBytesAsync(testFile1);
            Assert.Equal(payload, readBack);

            // Seek + Tell test
            ulong fh = await afc.OpenFileHandleAsync(testFile1, AfcFileOpenMode.ReadOnly);
            try
            {
                await afc.SeekFileAsync(fh, 1_100_000, SeekOrigin.Begin);
                ulong pos = await afc.TellFileAsync(fh);
                Assert.Equal(1_100_000UL, pos);
                byte[] slice = await afc.ReadFileChunkAsync(fh, 64);
                Assert.Equal(payload.AsSpan(1_100_000, 64).ToArray(), slice);
            }
            finally
            {
                await afc.CloseFileHandleAsync(fh);
            }

            // Rename + verify + remove
            await afc.RenamePathAsync(testFile1, testFile2);
            byte[] renamedRead = await afc.ReadAllBytesAsync(testFile2);
            Assert.Equal(payload, renamedRead);

            await afc.RemovePathAsync(testFile2);
            await afc.RemovePathAsync(emptyFile);
            output.WriteLine($"[AFC] Verified empty (0 B) + multi-chunk ({payload.Length:N0} B) write/read/seek/tell/rename/delete!");
        }
        finally
        {
            await afc.RemovePathAndContentsAsync(testDir);
        }
    }
}
