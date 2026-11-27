# 任务 C · Bonjour 局域网发现

请现在开始在 `iMobileDevice.Managed` 实现这张任务卡，运行回归测试，提交并推送功能分支，最后创建目标为 `main` 的 PR。目标是让业务程序在限时浏览窗口内拿到 iOS 服务广播的真实地址、端口和可用名称。

## 从这里开始

本机已为你准备好独立工作树。在 PowerShell 中运行以下命令，确认当前分支为 `codex/p1-bonjour`。

```powershell
Set-Location (Join-Path $env:USERPROFILE '.codex/worktrees/agy-bonjour/iMobileDevice')
git status --short --branch
```

如果在另一台机器执行，请在 `xutongqing/imobiledevice-net` 的独立检出目录使用同名分支。远端已有该分支时接着做，否则从 `origin/main` 创建。仓库默认分支是 `master`，本任务的集成目标是 `main`。

## 实现范围

- 在 `iMobileDevice.Managed/Bonjour/` 提供可取消、有超时的 mDNS/DNS-SD 发现 API。对齐 `bonjour remotepairing` 的地址与端口，以及 `bonjour remotepairing-manual-pairing` 的地址、端口和 TXT 名称。服务类型以固定基线的上游常量为准。
- 为 mobdev2 提供广播端点结果。上游 `bonjour mobdev2` 还会用配对凭据连接 Wi-Fi Lockdown 并读取设备摘要，这部分和 RSD 查询排在后续 PR。本次交接应将 mobdev2 标为部分覆盖。
- 正确关联服务实例、目标主机、端口和 IPv4/IPv6 地址。保留 IPv6 链路本地地址的接口作用域，合并多次广播，并保留不同接口上实际可达的端点。超时按约定返回本次发现，调用方取消时及时退出并释放资源。

## 验收与交付

- 在 `iMobileDevice.Managed.Tests/` 新建 Bonjour 专属测试。用固定 DNS-SD 报文或可控应答源覆盖分包到达的服务记录、带名称和缺名称的记录、双栈、多接口重复广播、相同地址不同 IPv6 作用域、无结果超时和主动取消。
- 让测试能在没有局域网设备的 CI 中稳定完成。交接时给出列举两类 RemotePairing 服务的调用示例，并写清 Windows 实网对照所需的广播条件，供主线程与 pymobiledevice3 同网验收。

运行 Managed 无硬件测试，并在 PR 中附上实跑结果。

```powershell
dotnet test iMobileDevice.Managed.Tests/iMobileDevice.Managed.Tests.csproj -c Release --filter "FullyQualifiedName!~ManagedDeviceE2ETests&FullyQualifiedName!~RsdAndDvtE2ETests"
```

推送 `codex/p1-bonjour`。有权限时开目标为 `main` 的 PR；缺少开 PR 的工具时，交回已推送的分支名和提交 SHA 即可。交接时附公开 API 调用示例、对应上游操作、测试结果和待真机验收项。主线程统一更新功能对照表、处理 NuGet 和 CI、安排 Windows 真机并合并。

上游对照固定在 `9d54c30`。[Bonjour 命令](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/bonjour.py) · [Bonjour 发现实现](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/bonjour.py)
