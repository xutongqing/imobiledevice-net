# imobiledevice-net

[![Build Native Libraries & NuGet Package](https://github.com/xutongqing/imobiledevice-net/actions/workflows/build-native-and-pack.yml/badge.svg)](https://github.com/xutongqing/imobiledevice-net/actions/workflows/build-native-and-pack.yml)

在 Windows、macOS 和 Linux 上使用 .NET / C# 与 iOS 设备通信的开源库，基于 `libimobiledevice-win32/imobiledevice-net` 演进维护。

本仓库现在同时提供 **两套互补方案**：

| 项目 | 定位 | 依赖 | 亮点能力 |
| --- | --- | --- | --- |
| **[`iMobileDevice.Managed`](iMobileDevice.Managed/)** | **纯 C# 托管协议层（`net8.0`，推荐用于新功能与 iOS 17+/18）** | **零原生库、零外部 NuGet、无需 root / 虚拟网卡驱动** | 纯托管 `Plist`（XML + `bplist00`）、`Usbmuxd`、`Lockdown`（配对 + 证书绑定 `SslStream` TLS）、`AFC` 文件系统、以及 **iOS 17.4+/18 `CoreDeviceProxy` + 用户态 IPv6/TCP 隧道 + HTTP/2 `RemoteXPC` + `RSD` + `DVT` 真机截屏** |
| **[`iMobileDevice-net`](iMobileDevice-net/)** | **完整上游 C 库 P/Invoke 绑定（`net45` / `netstandard2.0` / `netcoreapp3.0`）** | 随 NuGet 分发原生动态库 | 完整同步上游 `libimobiledevice 1.4.0` + `libplist` + `libusbmuxd` + `libirecovery` + `libideviceactivation`（覆盖全部 28 个公开头文件、332 个 API，新增 `BtPacketLogger`、`OsTrace`、`ReverseProxy`），GitHub Actions 自动构建 **Windows (`win-x64`) + Linux (`linux-x64`) + macOS (`osx-arm64`)** 原生库与 CLI 工具 |

> **运行前提说明**：无论是纯 C# 的 `iMobileDevice.Managed` 还是原生绑定的 `iMobileDevice-net`，常规 USB 通信仍依赖宿主机的设备守护服务：
> - **macOS**：系统内置 `/var/run/usbmuxd`（开箱即用）
> - **Linux**：需运行 `usbmuxd`
> - **Windows**：需安装 Apple Mobile Device Support（iTunes 或 Microsoft Store 版 Apple Devices，监听 `127.0.0.1:27015`）
> - **iOS 开发者服务**需按服务和系统版本准备环境。对照基线中，iOS 17+ 的 DVT 截屏需开启开发者模式，CoreDevice 开发者服务另需挂载个性化开发者镜像。本仓库的真机覆盖情况见[功能对照表](docs/managed-parity.md)。

---

## 一、快速上手：`iMobileDevice.Managed`（纯 C#，零原生依赖）

连接多台设备时，使用 `SelectDeviceAsync` 或 `ConnectByUdidAsync` 指定 UDID；`DeviceConnectionDiagnostics.InspectAsync` 可查看每条连接的摘要和配对会话状态。[P1 接口与 Windows 真机验证](docs/managed-device-discovery.md)

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

### `iMobileDevice.Managed` 模块清单

- **`Plist/`**：纯 C# XML 1.0 与二进制 `bplist00` 编解码（支持有符号/无符号 64 位整数、UTF-16BE、Apple 2001 纪元日期、UID）
- **`Usbmuxd/`**：跨平台 Unix Domain Socket / Windows TCP `27015` 客户端，支持设备列表、实时热插拔流 `WatchDevicesAsync`、`ReadBUID`、`Read/Save/DeletePairRecord` 与端口转发
- **`Lockdown/`**：`QueryType`、纯 BCL RSA 证书生成与 `PairAsync`、对端证书绑定校验的 `SslStream` TLS 会话、并发请求串行化、`StopSession` TLS 降级与 `ConnectToServiceAsync`
- **`Afc/`**：40 字节 `CFA6LPAA` 帧协议，支持设备信息、目录列举、文件属性、句柄 Seek/Tell、分块读写、重命名与递归删除
- **`Tunnel/`**：`CoreDeviceProxyClient`（`CDTunnel` 握手）+ `UserspaceTcpTunnel`（纯用户态 IPv6/TCP 栈）
- **`RemoteXpc/`**：Apple XPC 二进制序列化、`RemoteXpcConnection`（明文 HTTP/2 帧、流控窗口、并发 RPC 路由）与 `RemoteServiceDiscoveryClient`（RSD）
- **`Dvt/`**：`NsKeyedArchiver`、`DtxConnection`（多片重组与大小校验）、`DvtClient`（`TakeScreenshotAsync` 及通用通道调用）

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

纯 C# 项目从同一工作流的 `imobiledevice-managed-nupkg` 制品获取独立的 `iMobileDevice.Managed` 包（`net8.0`）。将制品解压到本地 NuGet 源后，按实际版本安装：

```bash
dotnet add package iMobileDevice.Managed --version <包版本> --source <本地包源目录>
```

普通 CI 构建使用 `0.1.0-ci.<run>.<attempt>`，版本标签 `vX.Y.Z` 构建使用 `X.Y.Z`。工作流验证新项目仅从临时包源安装并运行，再上传制品。[Managed 实施路线图](docs/managed-roadmap.md) · [pymobiledevice3 逐项对照](docs/managed-parity.md)

每次推送到 `master` 或打 `v*` 标签都会触发 GitHub Actions [Build Native Libraries & NuGet Package](https://github.com/xutongqing/imobiledevice-net/actions/workflows/build-native-and-pack.yml)，自动产出以下 Artifact：

- `imobiledevice-net-nupkg`：同时内嵌 `win-x64`、`linux-x64`、`osx-arm64` 原生库与 CLI 工具的 NuGet 包
- `native-win-x64` / `native-linux-x64` / `native-osx-arm64`：各平台独立动态库与命令行工具

---

## 许可证

本仓库沿用 [LGPL-2.1](LICENSE.txt) 许可证。`iMobileDevice.Managed` 为独立净室（clean-room）C# 实现。
