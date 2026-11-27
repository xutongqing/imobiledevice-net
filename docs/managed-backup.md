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

Windows 如有文件正在被读取，目录重命名可能被拒绝。客户端会先重试，再尝试逐文件发布，在所有文件就位后写入完成状态。发布遇到其他错误时会保留已验证的工作快照，下次用同一根目录和来源标识打开操作时可继续发布。这条备用路径需要额外一份快照的磁盘空间。

## 验证范围

协议回归覆盖分帧传输、空文件、批量下载中的缺失文件、空间不足、传输中断后重试，以及已知密码和错误密码。恢复测试从公开 API 连接本地模拟设备，验证携带 EscrowBag、目标与来源选择、同步锁、恢复文件字节、设备成功或拒绝，以及源快照保持完整。[传输与存储测试](../iMobileDevice.Managed.Tests/MobileBackupTests.cs) · [恢复会话测试](../iMobileDevice.Managed.Tests/MobileBackupRestoreTests.cs)

### Windows USB 真机验收（2026-11-27）

Windows 11 x64（26H2，内部版本 26300）通过 USB 连接已配对的 `iPhone17,1`，系统为 iOS 27.3。设备已启用本地备份加密。Windows 本地通过 43 项无硬件回归测试。实现提交 `a7cb8a0` 的 Windows、Linux、macOS 构建、独立 Managed 包和原生包作业均通过。[GitHub Actions #19](https://github.com/xutongqing/imobiledevice-net/actions/runs/45953531802)

| 操作 | 实测结果 |
| --- | --- |
| Managed 全量加密备份 | 在新目录完成并发布。`SnapshotState=finished`、`IsEncrypted=true`。72,547 个数据文件共 9.13 GiB，另有 4 个元数据文件，其中 `Manifest.db` 为 139,309,072 字节。 |
| Managed 增量加密备份 | 使用仅引用独立 NuGet 的 .NET 8 程序，对全量快照调用 `BackupAsync(..., full: false)`。新快照完成，数据文件 72,547 个，`Manifest.db` 为 138,154,000 字节。使用的包为 [`iMobileDevice.Managed.0.1.0-ci.19.1`](https://github.com/xutongqing/imobiledevice-net/actions/runs/45953531802/artifacts/15177737386)。 |
| pymobiledevice3 续做增量 | 将同一份 Managed 全量快照复制到独立目录，随后在同一台手机上用 pymobiledevice3 11.57.0 执行 `backup2 backup --no-full`。退出码为 0，结果为已完成的加密快照，`Manifest.db` 为 138,162,192 字节。 |

两次增量结果的 72,547 个数据文件标识全部相同。按标识均匀抽取 128 个文件，再加入体积最大的 4 个，逐一计算磁盘密文的 SHA-256。132 个样本中有 131 个相同、1 个不同。两次增量按先后顺序运行，本次未解密真机清单，无法据此确定差异涉及的逻辑文件或原因。

恢复已通过模拟设备端到端测试。真机恢复需单独指定可覆盖的测试设备，macOS 真机测试也仍待验收。选择性备份规则、旧加密 MBDB 清单列举、iCloud 设置和抹除设备的进展见[功能对照表](managed-parity.md)。
