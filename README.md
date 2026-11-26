# imobiledevice-net

[![Build Native Libraries & NuGet Package](https://github.com/xutongqing/imobiledevice-net/actions/workflows/build-native-and-pack.yml/badge.svg)](https://github.com/xutongqing/imobiledevice-net/actions/workflows/build-native-and-pack.yml)

在 Windows、macOS 和 Linux 上使用 .NET / C# 与 iOS 设备通信的开源库，基于 `libimobiledevice-win32/imobiledevice-net` 演进维护。

本仓库现在同时提供 **两套互补方案**：

| 项目 | 定位 | 依赖 | 亮点能力 |
| --- | --- | --- | --- |
| **[`iMobileDevice.Managed`](iMobileDevice.Managed/)** | **纯 C# 托管协议层（`net8.0`，推荐用于新功能与 iOS 17+/18）** | **零原生库、零外部 NuGet、无需 root / 虚拟网卡驱动** | 纯托管 `Plist`（XML + `bplist00`）、`Usbmuxd`、`Lockdown`（配对 + 证书绑定 `SslStream` TLS）、`AFC` 文件系统、以及 **iOS 17.4+/18 `CoreDeviceProxy` + 用户态 IPv6/TCP 隧道 + HTTP/2 `RemoteXPC` + `RSD` + `DVT` 真机截屏、进程列表与 CPU / 内存采样** |
| **[`iMobileDevice-net`](iMobileDevice-net/)** | **完整上游 C 库 P/Invoke 绑定（`net45` / `netstandard2.0` / `netcoreapp3.0`）** | 随 NuGet 分发原生动态库 | 完整同步上游 `libimobiledevice 1.4.0` + `libplist` + `libusbmuxd` + `libirecovery` + `libideviceactivation`（覆盖全部 28 个公开头文件、332 个 API，新增 `BtPacketLogger`、`OsTrace`、`ReverseProxy`），GitHub Actions 自动构建 **Windows (`win-x64`) + Linux (`linux-x64`) + macOS (`osx-arm64`)** 原生库与 CLI 工具 |

> **运行前提说明**：无论是纯 C# 的 `iMobileDevice.Managed` 还是原生绑定的 `iMobileDevice-net`，常规 USB 通信仍依赖宿主机的设备守护服务：
> - **macOS**：系统内置 `/var/run/usbmuxd`（开箱即用）
> - **Linux**：需运行 `usbmuxd`
> - **Windows**：需安装 Apple Mobile Device Support（iTunes 或 Microsoft Store 版 Apple Devices，监听 `127.0.0.1:27015`）
> - **iOS 17+/18 DVT 开发者服务（如真机截屏）**：需在设备开启“开发者模式”并挂载对应 Personalized Developer Disk Image（可使用本仓库产物中的 `idevicedevmodectl` 与 `ideviceimagemounter` 完成挂载）

---

## 一、快速上手：`iMobileDevice.Managed`（纯 C#，零原生依赖）

### 1. USB 设备发现 + Lockdown TLS 会话 + AFC 文件读写

```csharp
using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Usbmuxd;

var usbmuxd = new UsbmuxdClient();

// 1. 枚举已连接的 USB 设备
var devices = await usbmuxd.ListDevicesAsync();
foreach (var dev in devices)
{
    Console.WriteLine($"DeviceID={dev.DeviceId}, UDID={dev.SerialNumber}, Type={dev.ConnectionType}");
}

// 2. 建立 Lockdown 配对会话（自动通过 .NET SslStream 完成证书绑定 TLS 握手）
await using var lockdown = await LockdownClient.ConnectFirstDeviceAsync(usbmuxd, handshake: true);
Console.WriteLine($"设备名: {await lockdown.GetStringValueAsync("DeviceName")}");
Console.WriteLine($"型号:   {await lockdown.GetStringValueAsync("ProductType")}");
Console.WriteLine($"系统:   iOS {await lockdown.GetStringValueAsync("ProductVersion")}");

// 3. 启动 AFC 服务并读写文件
await using var afc = await AfcClient.ConnectAsync(lockdown);
var entries = await afc.ReadDirectoryAsync("/");
Console.WriteLine("根目录: " + string.Join(", ", entries));

await afc.WriteAllBytesAsync("/hello.txt", "Hello from pure C#!"u8.ToArray());
byte[] content = await afc.ReadAllBytesAsync("/hello.txt");
await afc.RemovePathAsync("/hello.txt");
```

### 2. iOS 17.4+ / 18 真机截屏（纯 C# 用户态隧道 + RSD + DVT，无需 root）

```csharp
using iMobileDevice.Managed.Dvt;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.RemoteXpc;
using iMobileDevice.Managed.Tunnel;
using iMobileDevice.Managed.Usbmuxd;

var usbmuxd = new UsbmuxdClient();
await using var lockdown = await LockdownClient.ConnectFirstDeviceAsync(usbmuxd, handshake: true);

// 1. 建立 CoreDeviceProxy L3 隧道 + 纯 C# 用户态 IPv6/TCP 协议栈（无需 utun/wintun，无需管理员权限）
await using var proxy = await CoreDeviceProxyClient.ConnectAsync(lockdown);
await using var tunnel = new UserspaceTcpTunnel(proxy);

// 2. 通过 HTTP/2 RemoteXPC 发现 RSD 服务列表
await using var rsd = await RemoteServiceDiscoveryClient.ConnectAsync(tunnel);
Console.WriteLine($"RSD 发现 {rsd.Services.Count} 个服务（iOS {rsd.OsVersion}）");

// 3. 连接 com.apple.instruments.dtservicehub 并截取原图 PNG
await using var dvt = await DvtClient.ConnectAsync(rsd);
byte[] png = await dvt.TakeScreenshotAsync();
await File.WriteAllBytesAsync("screenshot.png", png);
```

### 3. 按 UDID 选择 USB 设备，列出进程并采集 CPU 和内存

在 .NET 8 项目中，从工作流产物或本地构建的 NuGet 包安装托管库。以下命令假设包位于 `./nupkg`。

```bash
dotnet add package iMobileDevice.Managed --source ./nupkg --prerelease
```

设备需要已配对，开启开发者模式并挂载开发者镜像。将上面设备枚举中的 `SerialNumber` 作为第一个命令行参数传入。

```csharp
using iMobileDevice.Managed.Dvt;

await using var dvt = await DvtClient.ConnectUsbAsync(args[0]);
var processes = await dvt.ListProcessesAsync();
Console.WriteLine($"当前运行 {processes.Count} 个进程");
foreach (var process in processes)
    Console.WriteLine($"{process.ProcessId,6} {process.Name}");

using var sampling = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    await foreach (var batch in dvt.WatchProcessesAsync(1000, sampling.Token))
    {
        foreach (var p in batch.Where(p => p.CpuPercent > 0))
            Console.WriteLine($"{p.ProcessId,6} {p.Name}  CPU {p.CpuPercent:F1}%  占用 {p.PhysicalFootprintBytes:N0} B");
    }
}
catch (OperationCanceledException) when (sampling.IsCancellationRequested) { }
```

CPU 按单个核心的百分比计量，多核进程可以超过 100%。内存取 Instruments 的 `physFootprint`，单位为字节。缺失的数值返回 `null`。采样会跳过 CPU 预热的首批，每批只包含设备在该间隔上报的 PID。某个 PID 缺席不代表进程已退出。消费速度过慢时会丢弃旧批次。取消或退出循环后，可以在同一会话继续列进程。连接中断时，读取会以异常结束。

### 4. 按 Bundle ID 监控单个 App

将设备 UDID 和目标 App 的 Bundle ID 作为两个命令行参数传入。对于设备提供了标识的进程，可以从 `ListProcessesAsync()` 返回的 `BundleIdentifier` 读取这个值。只想查询当前主进程 PID 时，调用 `GetApplicationProcessIdAsync(bundleId)` 即可。

```csharp
using iMobileDevice.Managed.Dvt;

await using var dvt = await DvtClient.ConnectUsbAsync(args[0]);
int batches = 0;
await foreach (var app in dvt.WatchApplicationAsync(args[1], intervalMilliseconds: 500))
{
    if (app.ProcessId is null)
        Console.WriteLine("App 未运行");
    else if (app.Measurement is { } m)
        Console.WriteLine($"PID {m.ProcessId}  CPU {m.CpuPercent:F1}%  占用 {m.PhysicalFootprintBytes:N0} B");
    else
        Console.WriteLine($"PID {app.ProcessId} 正在运行，本批暂无测量值");

    if (++batches == 10) break;
}
```

采样可以在 App 打开前开始。每个收到的批次都会向 Instruments 重新查询一次 PID。观察到 PID 变化时先返回新 PID，后续批次再报告它的 CPU 和内存。App 在两次查询之间的短暂启动和退出可能观察不到。

### `iMobileDevice.Managed` 模块清单

- **`Plist/`**：纯 C# XML 1.0 与二进制 `bplist00` 编解码（支持有符号/无符号 64 位整数、UTF-16BE、Apple 2001 纪元日期、UID）
- **`Usbmuxd/`**：跨平台 Unix Domain Socket / Windows TCP `27015` 客户端，支持设备列表、实时热插拔流 `WatchDevicesAsync`、`ReadBUID`、`Read/Save/DeletePairRecord` 与端口转发
- **`Lockdown/`**：`QueryType`、纯 BCL RSA 证书生成与 `PairAsync`、对端证书绑定校验的 `SslStream` TLS 会话、并发请求串行化、`StopSession` TLS 降级与 `ConnectToServiceAsync`
- **`Afc/`**：40 字节 `CFA6LPAA` 帧协议，支持设备信息、目录列举、文件属性、句柄 Seek/Tell、分块读写、重命名与递归删除
- **`Tunnel/`**：`CoreDeviceProxyClient`（`CDTunnel` 握手）+ `UserspaceTcpTunnel`（纯用户态 IPv6/TCP 栈）
- **`RemoteXpc/`**：Apple XPC 二进制序列化、`RemoteXpcConnection`（明文 HTTP/2 帧、流控窗口、并发 RPC 路由）与 `RemoteServiceDiscoveryClient`（RSD）
- **`Dvt/`**：`NsKeyedArchiver`、`DtxConnection`（多片重组与大小校验）、`DvtClient`（按 UDID 连接、截屏、进程列表、整机和单个 App 的性能采样）

---

## 二、快速上手：`iMobileDevice-net`（上游原生库 P/Invoke 绑定）

适用于需要调用全部 `libimobiledevice 1.4.0` 传统服务（如 `MobileBackup2`、`MobileImageMounter`、`OsTrace`、`BtPacketLogger`、`InstallationProxy`、`NotificationProxy`、`MobileSync` 等）的场景。

### 同步与改进要点

- **完整同步上游 1.4.0**：补齐 `BtPacketLogger`、`OsTrace`、`ReverseProxy` 三个新模块，以及 `iDevice`、`Lockdown`、`MobileImageMounter`（含 iOS 17+ Personalized DDI 查询 API）、`Afc`、`DebugServer`、`PropertyListService`、`Service`、`libplist`、`libirecovery`、`libusbmuxd` 新增接口。
- **ABI 与安全性修复**：修复 64 位 `size_t*` / `UIntPtr` 宽度、函数指针按值传递、空指针结尾字符串数组封送、可变参数托管封装、异步回调委托 GC 固定，以及 `LockdownServiceDescriptor.identifier`、`AfcError.SslError`、`NotificationProxyNotifications` 常量补齐。
- **跨平台原生构建与打包**：GitHub Actions 工作流 [`.github/workflows/build-native-and-pack.yml`](.github/workflows/build-native-and-pack.yml) 自动从上游编译并验证三平台原生动态库与全部 CLI 工具（`idevice_id`、`ideviceinfo`、`idevicebackup2`、`ideviceimagemounter`、`idevicedevmodectl` 等），统一打包至 `iMobileDevice-net.*.nupkg` 的 `runtimes/<rid>/native/`（支持 `win-x64`、`linux-x64`、`osx-arm64`）。

### 示例：枚举设备并读取名称

```csharp
using System.Collections.ObjectModel;
using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;

NativeLibraries.Load();

var idevice = LibiMobileDevice.Instance.iDevice;
var lockdown = LibiMobileDevice.Instance.Lockdown;

int count = 0;
var ret = idevice.idevice_get_device_list(out ReadOnlyCollection<string> udids, ref count);
if (ret == iDeviceError.NoDevice)
{
    return;
}

ret.ThrowOnError();

foreach (var udid in udids)
{
    idevice.idevice_new(out iDeviceHandle deviceHandle, udid).ThrowOnError();
    using (deviceHandle)
    {
        lockdown.lockdownd_client_new_with_handshake(deviceHandle, out LockdownClientHandle lockdownHandle, "MyApp").ThrowOnError();
        using (lockdownHandle)
        {
            lockdown.lockdownd_get_device_name(lockdownHandle, out string deviceName).ThrowOnError();
            Console.WriteLine($"{udid}: {deviceName}");
        }
    }
}
```

---

## 三、构建与测试

### 1. 运行纯 C# 单元测试（无需原生库、无需真机）

```bash
dotnet test iMobileDevice.Managed.Tests/iMobileDevice.Managed.Tests.csproj -c Release
```

### 2. 运行纯 C# 真机端到端验收（连接 USB iPhone）

```bash
IMOBILEDEVICE_RUN_DEVICE_E2E=1 \
IMOBILEDEVICE_SCREENSHOT_OUT=./screenshot.png \
  dotnet test iMobileDevice.Managed.Tests/iMobileDevice.Managed.Tests.csproj -c Release
```

只验证进程监控和断连时可运行以下命令。接入多台 USB 手机时，通过环境变量 `IMOBILEDEVICE_UDID` 指定其中一台。

```bash
IMOBILEDEVICE_RUN_DEVICE_E2E=1 dotnet test iMobileDevice.Managed.Tests -c Release --filter FullyQualifiedName~DvtProcessMonitorE2ETests
```

### 3. 运行原生绑定单元测试与真机冒烟测试

```bash
# 仅托管签名与单元测试
DOTNET_ROLL_FORWARD=Major \
  dotnet test iMobileDevice.Tests/iMobileDevice.Tests.csproj -c Release \
  --filter "FullyQualifiedName~UpstreamSyncTests|FullyQualifiedName~HandleTests"

# 启用真实原生库冒烟测试（需将对应平台原生库放在输出目录或库搜索路径）
DOTNET_ROLL_FORWARD=Major IMOBILEDEVICE_RUN_NATIVE_SMOKE=1 \
  dotnet test iMobileDevice.Tests/iMobileDevice.Tests.csproj -c Release \
  --filter "FullyQualifiedName~NativeSmokeTests"

# 启用原生绑定只读真机端到端测试
DOTNET_ROLL_FORWARD=Major IMOBILEDEVICE_RUN_DEVICE_E2E=1 \
  dotnet test iMobileDevice.Tests/iMobileDevice.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DeviceE2ETests"
```

---

## 四、获取预编译 NuGet 包与原生产物

每次推送到 `master` 或打 `v*` 标签都会触发 GitHub Actions [Build Native Libraries & NuGet Package](https://github.com/xutongqing/imobiledevice-net/actions/workflows/build-native-and-pack.yml)，自动产出以下 Artifact：

- `imobiledevice-net-nupkg`：包含纯 C# 的 `iMobileDevice.Managed` 包，以及内嵌 `win-x64`、`linux-x64`、`osx-arm64` 原生库与 CLI 工具的 `iMobileDevice-net` 包
- `native-win-x64` / `native-linux-x64` / `native-osx-arm64`：各平台独立动态库与命令行工具

---

## 许可证

本仓库沿用 [LGPL-2.1](LICENSE.txt) 许可证。`iMobileDevice.Managed` 为独立净室（clean-room）C# 实现。
