# iMobileDevice.Managed

.NET 8 的 iOS 设备协议库。提供 USB 枚举与热插拔、Lockdown 配对和 TLS、AFC 文件读写，以及 iOS 17.4+ USB 用户态隧道、RSD 服务发现和 DVT 截屏。

```csharp
using iMobileDevice.Managed.Usbmuxd;

var usbmuxd = new UsbmuxdClient();
foreach (var device in await usbmuxd.ListDevicesAsync())
    Console.WriteLine($"{device.SerialNumber} ({device.ConnectionType})");
```

Windows 的普通 USB 连接依赖 iTunes 提供的 Apple Mobile Device Service，Linux 需要运行 usbmuxd，macOS 使用系统服务。需要配对的操作还需在设备上确认信任。

连接指定设备使用 `LockdownClient.ConnectByUdidAsync(udid)`。需要诊断时，`DeviceConnectionDiagnostics.InspectAsync` 可按 UDID 和连接类型筛选，返回每条连接的配对状态、设备摘要和错误码。[选机与连接诊断](https://github.com/xutongqing/imobiledevice-net/blob/master/docs/managed-device-discovery.md)

从仓库 GitHub Actions 的 `imobiledevice-managed-nupkg` 制品获取 NuGet 包，解压到本地包源后运行 `dotnet add package iMobileDevice.Managed --version <包版本> --source <本地包源目录>`。

[使用示例](https://github.com/xutongqing/imobiledevice-net#readme) · [功能对照与平台条件](https://github.com/xutongqing/imobiledevice-net/blob/master/docs/managed-parity.md)
