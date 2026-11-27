# 选择 iPhone 与诊断连接

业务程序可以先枚举 usbmuxd 已发现的所有连接，再使用用户选择的 UDID 建立 Lockdown 会话。每条记录包含设备序列号（UDID）、临时连接 ID 和连接类型。同一台设备的 USB 与网络连接各占一行。跨次连接应保存 UDID，每次重新枚举得到当前连接 ID。

```csharp
using iMobileDevice.Managed.Diagnostics;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Usbmuxd;

var mux = new UsbmuxdClient();
var usbDevices = await mux.ListDevicesByConnectionTypeAsync(UsbmuxdConnectionType.Usb);
foreach (var device in usbDevices)
    Console.WriteLine($"{device.SerialNumber}  {device.ConnectionType}");

// selectedUdid 来自设备列表中用户选中的一行。
async Task ConnectSelectedAsync(string selectedUdid, CancellationToken ct)
{
    await using var session = await LockdownClient.ConnectByUdidAsync(
        selectedUdid, mux, UsbmuxdConnectionType.Usb, cancellationToken: ct);
    Console.WriteLine(await session.GetStringValueAsync("ProductType", cancellationToken: ct));
}
```

`SelectDeviceAsync(udid, connectionType)` 允许省略任一筛选条件。匹配时忽略 UDID 的大小写、连字符和首尾空白，返回设备记录时保留原始序列号。优先选择第一条匹配的 USB 连接，否则取最后一条匹配连接。无匹配时返回 `null`；`ConnectByUdidAsync` 在这种情况下抛出错误码为 `DeviceNotFound` 的 `LockdownException`。原有的 `ConnectFirstDeviceAsync` 仍采用原先的选机顺序。

## 连接诊断

```csharp
var report = await DeviceConnectionDiagnostics.InspectAsync(mux, cancellationToken: ct);
Console.WriteLine($"{report.Endpoint}  {report.Status}");
foreach (var result in report.Devices)
    Console.WriteLine($"{result.Device.ConnectionType}  {result.Summary?.ProductType}  {result.Status}  {result.ErrorCode}");
```

仅需原始枚举时使用 `ListDevicesAsync`。需要摘要和诊断时使用 `InspectAsync`，也可传入 `udid` 和 `connectionType`。它先枚举，再逐条打开 Lockdown，读取已有配对记录并尝试建立会话。无记录时仅读取设备允许公开的摘要。诊断不会新建配对、写入配对库或启动开发者服务。

| 查看位置 | 结果及含义 |
| --- | --- |
| `report.Status` | `DaemonUnavailable` 表示宿主端点无法连接；`NoDevices` 表示 daemon 可连但枚举为空；`NoMatch` 表示有设备但筛选未命中；`DevicesFound` 表示每条命中连接都有独立结果 |
| `result.Status` | `Connected` 表示已有配对会话已建立并完成属性查询，TLS 状态见 `IsSslEnabled`。摘要提供设备名、设备类别、产品型号、系统版本、构建号和设备报告的 UDID |
| 配对和锁定 | `PairingRequired` 表示缺少本机记录或设备请求确认；`PairingRejected` 表示设备拒绝记录或配对请求；`DeviceLocked` 表示设备回复了锁定相关错误。成功会话本身不能判定屏幕锁定状态 |
| 单条连接失败 | `Disconnected` 表示 usbmuxd 报告设备已消失；`LockdownUnavailable` 表示 Lockdown 入口不可用；`AuthenticationFailed` 表示证书或 TLS 认证失败。已返回的底层错误码和说明见 `ErrorCode`、`Detail` |
| 超时与取消 | 默认给枚举和每条设备探测各 10 秒，可用 `timeout` 调整。超时记录为 `TimedOut`，其他通信或协议错误记录为 `Failed`。调用方取消则抛出 `OperationCanceledException` |

## Windows 真机验证 · 2026-11-27

在 Windows 11 x64 上，从独立的本地 Release NuGet（`0.1.0-p1.local`）还原到空包缓存，由 .NET 8.0.33 控制台运行以下检查。

| 检查 | 结果 |
| --- | --- |
| 列举和连接类型筛选 | 1 条 USB、0 条网络；全量诊断返回 1 条设备结果 |
| 按 UDID 连接 | 将枚举得到的 UDID 去掉连字符并转为小写；筛选命中同一 USB 连接，Lockdown TLS 会话读回的设备 UDID 与目标一致 |
| 摘要与诊断 | `Connected`，TLS 已启用；`iPhone17,1`，iOS `27.3`，构建 `24C5049d`。设备类别、名称及其余摘要字段均已读取 |
| 错误目标 | 不存在的 UDID 返回 `null` / `NoMatch`，连接返回 `DeviceNotFound`。限定当前设备走网络也返回 `NoMatch` |

现场连接了一台手机。多个物理设备、同一 UDID 的 USB/网络双路、错误选机、连接失效、设备锁定、缺少配对、拒绝旧记录、daemon 不可用、超时和取消的回归检查使用本地 TCP 测试端点。[测试代码](../iMobileDevice.Managed.Tests/DeviceDiscoveryTests.cs)
