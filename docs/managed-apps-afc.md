# 查询应用并传输共享文件

`InstallationProxyClient` 和 `AppContainer` 使用已配对的 Lockdown 会话。用户应用须允许文件共享才能通过 House Arrest 打开 Documents。Windows USB 使用 Apple Mobile Device Service。

```csharp
using iMobileDevice.Managed.Apps;
using iMobileDevice.Managed.Lockdown;

await using var device = await LockdownClient.ConnectByUdidAsync(udid, cancellationToken: token);
await using var apps = await InstallationProxyClient.ConnectAsync(device, token);
var matches = await apps.QueryAsync([bundleId], calculateSizes: true, cancellationToken: token);
if (matches.Count == 0) return;

await using var files = await AppContainer.OpenAsync(device, bundleId, AppContainerScope.Documents, token);
var entries = await files.ReadDirectoryAsync("/Documents", token);
var upload = await files.PushAsync(localSourceDirectory, "/Documents", cancellationToken: token);
var listing = await files.ListDirectoryAsync(upload.DestinationPath, recursive: true, cancellationToken: token);
var download = await files.PullAsync(upload.DestinationPath, localOutputDirectory, cancellationToken: token);
```

目录上传和下载都会在目标目录下创建源目录的同名子目录，实际落点见 `DestinationPath`。单个文件可传入明确的目标文件名。媒体目录可通过 `AfcClient.ConnectAsync(device, token)` 获取，使用相同的传输 API。需要包含起始目录时给列举 API 传入 `AfcListOptions.IncludeRoot = true`。下载默认遇错终止，设置 `AfcPullOptions.IgnoreErrors = true` 可继续处理其他文件，逐项结果见 `Failures` 和 `CompletedFiles`。

## Windows USB 验收

2026-11-27 在 Windows、`iPhone17,1`、iOS 27.3 上，用仅引用独立 Managed NuGet 的 .NET 8 程序执行。应用查询与同设备的 pymobiledevice3 11.57.0 对照，逐组比较完整 Bundle ID 集合。

| 操作 | 实测结果 |
| --- | --- |
| 应用枚举 | 默认 331 个，开启占位应用后 470 个。User 77、System 254、Hidden 0。各组 Bundle ID 集合均与上游一致。 |
| 多 Bundle ID、大小与回复分帧 | 已安装和虚构标识一起查询返回 1 项，含静态与动态大小字段；单独查询虚构标识返回 0 项。观察到的每次 Lookup 都收到一帧 `Status=Complete`，开启大小选项发出两次 Lookup。多帧与断线另由协议测试覆盖。 |
| House Arrest | 允许共享的应用在 `VendDocuments` 后可列举 `/Documents`，列举 `/` 返回 AFC `PermDenied`。虚构 Bundle ID 返回 `ApplicationLookupFailed`，受限系统应用返回 `InstallationLookupFailed`。 |
| 递归传输 | 在媒体目录和应用的 `/Documents` 各做一次上传、递归列举、下载。测试树含空目录、空文件及 2,097,183 字节的跨块文件。两条路径均通过目录核对和 SHA-256 校验，已清理设备测试目录。 |

当前真机证据覆盖 iOS 27.3 的 USB Lockdown 路径。iOS 26 的占位应用行为、RSD 服务路径、macOS 真机和允许整个容器的应用仍待验证。[协议与应用测试](../iMobileDevice.Managed.Tests/AppsProtocolTests.cs) · [递归传输测试](../iMobileDevice.Managed.Tests/AfcTransferTests.cs)
