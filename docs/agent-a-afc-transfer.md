# 任务 A · AFC 递归文件传输

请现在开始在 `iMobileDevice.Managed` 实现这张任务卡，运行回归测试，提交并推送功能分支，最后创建目标为 `main` 的 PR。目标是让业务调用方用同一套 C# 接口，在媒体目录和应用容器中列举、下载和上传目录树。

## 从这里开始

本机已为你准备好独立工作树。在 PowerShell 中运行以下命令，确认当前分支为 `codex/p2-afc-transfer`。

```powershell
Set-Location (Join-Path $env:USERPROFILE '.codex/worktrees/agy-afc-transfer/iMobileDevice')
git status --short --branch
```

如果在另一台机器执行，请在 `xutongqing/imobiledevice-net` 的独立检出目录使用同名分支。远端已有该分支时接着做，否则从 `origin/main` 创建。仓库默认分支是 `master`，本任务的集成目标是 `main`。

## 实现范围

- 在 `iMobileDevice.Managed/Afc/` 提供对应 `afc ls --recursive`、`afc pull`、`afc push` 的公开入口。以已有 `AfcClient` 为入口或参数，让 B 返回的应用容器会话可以直接复用。
- 复用 `ReadDirectoryAsync`、`GetFileInfoAsync`、文件句柄及分块读写。支持单文件、空文件、嵌套及空目录、大文件、进度与取消。对齐上游“目标已经是目录”的落点和覆盖行为。
- 下载默认遇错失败，提供等价于 `--ignore-errors` 的逐文件容错。取消应及时结束整个操作；中途出错时，调用方能辨别已完成与未完成的文件。保留底层 AFC 已有的取消及断线处理。
- 处理设备返回的目录项与链接，防止本地写出目标目录或遍历成环。由 B 负责打开应用容器，本卡只操作已经建立的 AFC 会话。

## 验收与交付

- 在 `iMobileDevice.Managed.Tests/` 新建专属传输测试。用模拟 AFC 会话跑一次目录树上传、列举和下载，核对相对路径、空目录、文件长度和 SHA-256。
- 覆盖大于一个传输块的文件、目标为已有目录、单文件失败后继续、途中取消及路径越界。把 B 可直接使用的接口签名写入 PR，Windows 真机往返由主线程联验。

运行 Managed 无硬件测试，并在 PR 中附上实跑结果。

```powershell
dotnet test iMobileDevice.Managed.Tests/iMobileDevice.Managed.Tests.csproj -c Release --filter "FullyQualifiedName!~ManagedDeviceE2ETests&FullyQualifiedName!~RsdAndDvtE2ETests"
```

推送 `codex/p2-afc-transfer`。有权限时开目标为 `main` 的 PR；缺少开 PR 的工具时，交回已推送的分支名和提交 SHA 即可。交接时附公开 API 调用示例、对应上游操作、测试结果和待真机验收项。主线程统一更新功能对照表、处理 NuGet 和 CI、安排 Windows 真机并合并。

上游对照固定在 `9d54c30`。[AFC 命令](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py) · [AFC 服务](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/afc.py)
