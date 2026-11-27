# P3 完整备份与恢复

`iMobileDevice.Managed.Backup.MobileBackupClient` 使用已配对的 Lockdown 会话启动 `com.apple.mobilebackup2`，协商 DeviceLink 与 MobileBackup2 2.0/2.1。文件内容按块传输。Windows USB 需要 Apple Mobile Device Service。库和独立 NuGet 使用 .NET 8，自身无需 Python、SQLite 或 libimobiledevice 动态库。

## 创建备份

```csharp
using iMobileDevice.Managed.Backup;
using iMobileDevice.Managed.Lockdown;

await using var device = await LockdownClient.ConnectByUdidAsync(udid, cancellationToken: token);
var backup = new MobileBackupClient(device);
var progress = new Progress<double>(p => Console.WriteLine($"{p:F1}%"));
string snapshot = await backup.BackupAsync(backupRoot, full: true, progress: progress, cancellationToken: token);
```

把同一 `backupRoot` 和 `full: false` 传给下一次调用即可增量更新。首次运行自动做全量。结果目录是 `backupRoot/<设备标识>`，其中包含 `Info.plist`、`Status.plist`、`Manifest.plist`、清单数据库和设备生成的文件。设备可能要求用户在手机上输入锁屏密码确认备份。

`GetWillEncryptAsync` 读取当前设备的备份加密设置。设备开启加密时，全量和增量备份均会原样保存设备生成的加密数据。读取加密清单或恢复时使用备份密码。调用 `ChangePasswordAsync(oldPassword, newPassword)` 可以启用、更改或关闭加密，启用时旧密码传 `null`，关闭时新密码传 `null`。

## 恢复到指定设备

恢复会更改目标设备上的数据，默认完成后重启。调用方通过 Lockdown 选定目标设备，通过 `SourceIdentifier` 指定磁盘上的来源目录。省略来源时使用目标设备标识。

```csharp
await using var target = await LockdownClient.ConnectByUdidAsync(targetUdid, cancellationToken: token);
var client = new MobileBackupClient(target);
await client.RestoreAsync(backupRoot, new BackupRestoreOptions
{
    SourceIdentifier = sourceUdid,
    Password = backupPassword,
    PreserveSettings = true,
    ReinstallApplications = true,
    RemoveItemsNotRestored = false,
    Reboot = true,
}, progress, token);
```

客户端先检查完成状态、清单和加密密码参数，再打开恢复服务。应用重装使用源备份的应用元数据。恢复中断时本机源快照仍可重试；设备已经应用的变更由 iOS 管理。设备返回的错误码可从 `MobileBackupException.ErrorCode` 读取。

## 列举和提取

| 操作 | 入口 |
| --- | --- |
| 离线读取本机设备摘要 | `MobileBackupClient.ReadInfo(root, sourceUdid)` |
| 获取设备解释的备份信息、列举文件 CSV | `GetInfoAsync`、`ListAsync`。加密的 `Manifest.db` 需要备份密码，解密副本仅在本机工作目录中使用 |
| 离线提取明文备份内的一个文件 | `MobileBackupClient.ExtractFileAsync(root, sourceUdid, domain, relativePath, destination, token)` |
| 借助已连接设备按域与路径提取文件 | `ExtractAsync`，输出到调用方的流，支持传入备份密码 |
| 展开完整备份 | `UnpackAsync`，返回新建的输出目录 |

## 空间与中断

备份、恢复和导出使用同卷的私有工作目录 `.imobiledevice-managed/<来源标识>`。同一个备份目录同时只允许一个操作。增量、恢复和设备端导出需要为已有快照的工作副本预留空间。完整备份在设备报告成功且快照检查通过后才替换原备份。发布过程中若进程终止，下次访问会恢复发布状态并清理上一次的临时文件。调用方应选择仅授权用户可访问的本地磁盘目录。

## 验证范围

协议回归覆盖分帧传输、空文件、批量下载中的缺失文件、空间不足、传输中断后重试，以及已知密码和错误密码。恢复测试从公开 API 连接本地模拟设备，验证携带 EscrowBag、目标与来源选择、同步锁、恢复文件字节、设备成功或拒绝，以及源快照保持完整。[传输与存储测试](../iMobileDevice.Managed.Tests/MobileBackupTests.cs) · [恢复会话测试](../iMobileDevice.Managed.Tests/MobileBackupRestoreTests.cs)

真机恢复需单独指定可覆盖的测试设备。选择性备份规则、旧加密 MBDB 清单列举、iCloud 设置和抹除设备仍列在[功能对照表](managed-parity.md)中。
