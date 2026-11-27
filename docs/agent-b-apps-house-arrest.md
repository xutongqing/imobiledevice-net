# 任务 B · 应用查询与 House Arrest 容器

请现在开始在 `iMobileDevice.Managed` 实现这张任务卡，运行回归测试，提交并推送功能分支，最后创建目标为 `main` 的 PR。目标是让业务程序在一条已配对的 Lockdown 会话上查到应用，再打开该应用获准共享的目录。

## 从这里开始

本机已为你准备好独立工作树。在 PowerShell 中运行以下命令，确认当前分支为 `codex/p2-apps-house-arrest`。

```powershell
Set-Location (Join-Path $env:USERPROFILE '.codex/worktrees/agy-apps-house-arrest/iMobileDevice')
git status --short --branch
```

如果在另一台机器执行，请在 `xutongqing/imobiledevice-net` 的独立检出目录使用同名分支。远端已有该分支时接着做，否则从 `origin/main` 创建。仓库默认分支是 `master`，本任务的集成目标是 `main`。

## 实现范围

- 在 `iMobileDevice.Managed/Apps/` 提供 Installation Proxy 的公开 C# 客户端。对齐 `apps list` 的 System、User、Hidden、Any 类型、应用大小及占位应用选项，对齐 `apps query` 按一个或多个 Bundle ID 查询和可选大小字段的行为。
- 正确合并分批返回的应用数据，等设备明确完成后返回；向调用方区分查无匹配、设备报错和传输中断。可参考 `Backup/BackupInfo.cs` 的备份专用 Browse 请求，该文件的公共化由主线程在合并后处理。
- 用 House Arrest 的 `VendDocuments`、`VendContainer` 请求对应目录。成功后将同一条服务流交给已有的 `AfcClient(Stream)`；确认失败或取消时释放服务流，成功后由返回的会话负责释放。
- A 负责递归传输。你可以立即用已有 `AfcClient.ReadDirectoryAsync` 验证从应用查询到容器列举的完整调用路径。

## 验收与交付

- 在 `iMobileDevice.Managed.Tests/` 新建应用查询和容器测试。用模拟服务验证分批回复、类型及选项传递、多 Bundle ID、无匹配、应用不存在和容器访问被拒绝。
- 在成功的 House Arrest 回复后，在同一条模拟连接上执行一次 AFC 列目录，检查协议切换、取消和释放。交接时提供“按 Bundle ID 查询 → 打开 Documents → 列目录”的最小示例，注明 Windows 真机需有允许共享的测试应用。

运行 Managed 无硬件测试，并在 PR 中附上实跑结果。

```powershell
dotnet test iMobileDevice.Managed.Tests/iMobileDevice.Managed.Tests.csproj -c Release --filter "FullyQualifiedName!~ManagedDeviceE2ETests&FullyQualifiedName!~RsdAndDvtE2ETests"
```

推送 `codex/p2-apps-house-arrest`。有权限时开目标为 `main` 的 PR；缺少开 PR 的工具时，交回已推送的分支名和提交 SHA 即可。交接时附公开 API 调用示例、对应上游操作、测试结果和待真机验收项。主线程统一更新功能对照表、处理 NuGet 和 CI、安排 Windows 真机并合并。

上游对照固定在 `9d54c30`。[应用命令](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py) · [Installation Proxy](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/installation_proxy.py) · [House Arrest](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/house_arrest.py)
