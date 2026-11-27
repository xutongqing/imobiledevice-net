# pymobiledevice3 与 iMobileDevice.Managed 逐项对照

以上游 2026-11-26 的 pymobiledevice3 `9d54c30` 为固定基线，Managed 从 `fc78d8a` 起随实现更新。本页按实际命令路径列出 **30 个顶层分组、303 个末级操作**，每个命令链接到固定提交的实现。单命令分组按实际调用方式记为 `pcap`、`btlogger` 等。公开 SDK 服务另见 [API 与协议对照表](managed-api-parity.md)，实施顺序见[路线图](managed-roadmap.md)。

| 已有 C# 对应操作 | 部分覆盖 | 待实现 | 合计 |
| ---: | ---: | ---: | ---: |
| 19 | 25 | 259 | 303 |

**已有**表示公开 C# API 可以完成该操作，返回结构、字节或流均可。**部分**列出可复用入口和缺少的语义或版本路径。**待实现**表示尚缺该服务或工作流。状态按源码核对；真机验证独立记录。原生绑定、原生工具、只打开通用服务流均不自动计入业务功能覆盖。

**验证与条件**　USB 项目使用平台设备服务，配对时需信任。网络发现、开发者隧道、DDI 和恢复模式的宿主条件见[平台矩阵](managed-roadmap.md)。设备监督、WDA Runner 和开发签名应用等要求见各操作的上游链接。[Windows 真机验收记录](managed-windows-validation.md)列出了 CI 制品在 iOS 15.1 和 27.3 上实测的枚举、配对、AFC 与截屏结果，并注明旧系统截屏的限制。其他平台与版本的覆盖需分别验证。

**基线复核**　从 [`CLI_GROUPS`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/__main__.py) 进入各模块，沿 `add_typer` 递归取已注册叶子，并按 Typer 的单命令折叠规则还原命令路径。升级时用完整路径比较增删和改名，逐行复核参数、产物和阶段。表中阶段是补齐或回归该操作的目标。

## activation · 设备激活

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`activation state`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/activation.py#L18) | 读取激活状态 | 待实现 | — | P6 |
| [`activation activate`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/activation.py#L25) | 激活设备 | 待实现 | — | P6 |
| [`activation deactivate`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/activation.py#L45) | 取消激活 | 待实现 | — | P6 |
| [`activation itunes`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/activation.py#L52) | 设置 iTunes 已连接标记（iOS 4 之前） | 待实现 | — | P6 |

## afc · 媒体文件

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`afc shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py#L29) | 交互式文件 Shell | 部分 | [`AfcClient`](../iMobileDevice.Managed/Afc/AfcClient.cs) 提供文件操作，缺交互会话 | P2 |
| [`afc webdav`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py#L37) | 通过 WebDAV 挂载媒体目录 | 待实现 | — | P6 |
| [`afc pull`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py#L55) | 下载文件或目录树 | 已有 | `AfcClient.PullAsync` 支持递归、分块、进度和逐文件容错。[用法与验收](managed-apps-afc.md) | P2 |
| [`afc push`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py#L75) | 上传文件或目录树 | 已有 | `AfcClient.PushAsync` 上传文件或目录树，支持进度和取消。[用法与验收](managed-apps-afc.md) | P2 |
| [`afc ls`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py#L92) | 列目录和递归遍历 | 已有 | `EnumerateDirectoryAsync` 支持递归和 IncludeRoot。[用法与验收](managed-apps-afc.md) | P2 |
| [`afc rm`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/afc.py#L108) | 删除文件或目录树 | 已有 | [`RemovePathAsync / RemovePathAndContentsAsync`](../iMobileDevice.Managed/Afc/AfcClient.cs) | P2 |

## amfi · 开发者模式

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`amfi reveal-developer-mode`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/amfi.py#L22) | 显示开发者模式设置 | 待实现 | — | P4 |
| [`amfi enable-developer-mode`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/amfi.py#L32) | 启用开发者模式 | 待实现 | — | P4 |
| [`amfi developer-mode-status`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/amfi.py#L48) | 查询开发者模式 | 待实现 | — | P4 |
| [`amfi trust-app-signer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/amfi.py#L55) | 信任应用签名者 | 待实现 | — | P4 |

## apps · 应用和文件容器

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`apps list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L41) | 列已安装应用 | 已有 | `InstallationProxyClient.ListAsync` 支持类型、占位应用与大小选项。[用法与验收](managed-apps-afc.md) | P2 |
| [`apps query`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L72) | 按 Bundle ID 查询应用信息 | 已有 | `QueryAsync` 支持按多个 Bundle ID 查询及大小选项。[用法与验收](managed-apps-afc.md) | P2 |
| [`apps uninstall`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L90) | 卸载应用 | 待实现 | — | P2 |
| [`apps install`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L97) | 安装应用 | 待实现 | — | P2 |
| [`apps afc`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L128) | 打开应用文件容器 Shell | 部分 | `AppContainer.OpenAsync` 打开共享 Documents 或获准的整个容器；交互 Shell 待实现。[用法与验收](managed-apps-afc.md) | P2 |
| [`apps webdav`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L146) | 通过 WebDAV 挂载应用文件 | 待实现 | — | P6 |
| [`apps pull`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L168) | 下载应用文件 | 已有 | `AppContainer.OpenAsync` 后调用 `PullAsync`。[用法与验收](managed-apps-afc.md) | P2 |
| [`apps push`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L184) | 上传应用文件 | 已有 | `AppContainer.OpenAsync` 后调用 `PushAsync`。[用法与验收](managed-apps-afc.md) | P2 |
| [`apps rm`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L200) | 删除应用文件 | 已有 | `AppContainer.OpenAsync` 后调用 `RemovePathAndContentsAsync`。[用法与验收](managed-apps-afc.md) | P2 |
| [`apps install-record`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/apps.py#L215) | 查询应用安装记录 | 待实现 | — | P2 |

## backup2 · 备份与恢复备份

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`backup2 backup`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L99) | 全量或增量备份及选择规则 | 部分 | [全量、增量和设备加密备份，选择规则与清单裁剪待实现](managed-backup.md) | P3 |
| [`backup2 restore`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L174) | 从本机备份恢复 | 部分 | [RestoreAsync 支持来源、密码、重启、设置和移除选项，始终保留本机源快照](managed-backup.md) | P3 |
| [`backup2 info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L233) | 读取备份信息 | 已有 | [GetInfoAsync 或离线 ReadInfo](managed-backup.md) | P3 |
| [`backup2 list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L248) | 列举备份内容 | 部分 | [ListAsync 返回 CSV，支持明文和有密码的加密 Manifest.db，旧加密 MBDB 待验证](managed-backup.md) | P3 |
| [`backup2 unback`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L263) | 展开备份目录树 | 已有 | [UnpackAsync 返回新展开目录](managed-backup.md) | P3 |
| [`backup2 extract`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L278) | 提取备份文件 | 已有 | [ExtractAsync 按域与路径提取，ExtractFileAsync 可离线读取明文备份](managed-backup.md) | P3 |
| [`backup2 encryption`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L306) | 切换备份加密 | 已有 | [GetWillEncryptAsync 与 ChangePasswordAsync](managed-backup.md) | P3 |
| [`backup2 cloud`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L345) | 切换 iCloud 备份 | 待实现 | — | P3 |
| [`backup2 change-password`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L358) | 更改备份密码 | 已有 | [ChangePasswordAsync](managed-backup.md) | P3 |
| [`backup2 erase-device`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/backup.py#L376) | 通过备份服务抹除设备 | 待实现 | — | P3 |

## btlogger · 蓝牙日志

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`btlogger`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/btlogger.py#L39) | 采集蓝牙数据包 | 待实现 | — | P6 |

## bonjour · 网络发现

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`bonjour mobdev2`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/bonjour.py#L21) | 发现 Wi-Fi Lockdown 设备 | 部分 | `BrowseMobdev2Async` 发现广播端点，配对会话及设备摘要待补。[验收](managed-bonjour.md) | P1 |
| [`bonjour remotepairing`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/bonjour.py#L45) | 发现远程配对服务 | 部分 | `BrowseRemotePairingAsync` 返回地址和端口，多地址筛选与上游有差异。[验收](managed-bonjour.md) | P1 |
| [`bonjour remotepairing-manual-pairing`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/bonjour.py#L58) | 发现手工配对广播 | 部分 | `BrowseRemotePairingManualPairingAsync` 返回地址、端口和 TXT 名称，多地址筛选与上游有差异。[验收](managed-bonjour.md) | P1 |
| [`bonjour rsd`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/bonjour.py#L79) | 发现 RSD 服务 | 待实现 | — | P1 |

## companion · 伴随设备

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`companion list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/companion_proxy.py#L21) | 枚举已配对的伴随设备 | 待实现 | — | P6 |

## crash · 崩溃报告

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`crash clear`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L116) | 清理崩溃报告 | 待实现 | — | P2 |
| [`crash parse`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L145) | 解析指定报告 | 待实现 | — | P2 |
| [`crash parse-latest`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L175) | 解析最近的报告 | 待实现 | — | P2 |
| [`crash pull`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L225) | 下载报告 | 待实现 | — | P2 |
| [`crash shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L254) | 交互浏览报告目录 | 待实现 | — | P2 |
| [`crash webdav`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L262) | 通过 WebDAV 挂载报告 | 待实现 | — | P6 |
| [`crash ls`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L280) | 列出报告 | 待实现 | — | P2 |
| [`crash flush`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L298) | 移入待处理报告 | 待实现 | — | P2 |
| [`crash watch`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L306) | 监听新报告 | 待实现 | — | P2 |
| [`crash sysdiagnose`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/crash.py#L335) | 等待并下载系统诊断归档 | 待实现 | — | P2 |

## cryptex · 开发者 Cryptex

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`cryptex list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/cryptex.py#L40) | 列已安装镜像 | 待实现 | — | P6 |
| [`cryptex auto-install`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/cryptex.py#L48) | 自动安装开发者 Cryptex | 待实现 | — | P6 |
| [`cryptex personalization-identifiers`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/cryptex.py#L91) | 查询个性化标识 | 待实现 | — | P6 |
| [`cryptex nonce`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/cryptex.py#L99) | 读取 nonce | 待实现 | — | P6 |
| [`cryptex roll-nonce`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/cryptex.py#L114) | 更新 nonce | 待实现 | — | P6 |
| [`cryptex uninstall`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/cryptex.py#L130) | 卸载镜像 | 待实现 | — | P6 |

## developer · 开发者服务

上游基线中，iOS 16+ 的开发者操作需要开发者模式。iOS 17 之前的开发者服务需要 DDI；iOS 17+ 的 DVT 可仅开开发者模式，CoreDevice 需要 DDI。WDA 操作还需设备端已安装 Runner；内存分析需可调试的开发签名应用。下表分别跟踪传统 screenshotr、DVT、CoreDevice 和 WDA 四种截屏路径。[开发者服务版本条件](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/guides/cli-recipes.md)

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`developer shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/__init__.py#L82) | 连接开发者服务 Shell | 待实现 | — | P4 |
| [`developer screenshot`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/__init__.py#L133) | 通过传统 screenshotr 服务截屏 | 待实现 | — | P4 |
| [`developer dvt proclist`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L154) | 列运行进程 | 待实现 | — | P4 |
| [`developer dvt is-running-pid`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L167) | 检查 PID 是否运行 | 待实现 | — | P4 |
| [`developer dvt memlimitoff`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L178) | 调整进程内存限制 | 待实现 | — | P4 |
| [`developer dvt applist`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L189) | 列已安装应用 | 待实现 | — | P4 |
| [`developer dvt signal`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L234) | 发送进程信号 | 待实现 | — | P4 |
| [`developer dvt kill`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L271) | 按 PID 终止进程 | 待实现 | — | P4 |
| [`developer dvt process-id-for-bundle-id`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L282) | 用 Bundle ID 查 PID | 待实现 | — | P4 |
| [`developer dvt pkill`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L312) | 按名称匹配并终止进程 | 待实现 | — | P4 |
| [`developer dvt launch`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L343) | 启动应用 | 待实现 | — | P4 |
| [`developer dvt shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L387) | DVT 交互会话 | 部分 | [`OpenChannelAsync`](../iMobileDevice.Managed/Dvt/DvtClient.cs) 与 DTX 调用，缺交互会话 | P4 |
| [`developer dvt ls`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L422) | 通过 DVT 列目录 | 待实现 | — | P4 |
| [`developer dvt device-information`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L438) | 读取设备和内核信息 | 待实现 | — | P4 |
| [`developer dvt netstat`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L458) | 监听网络连接 | 待实现 | — | P4 |
| [`developer dvt screenshot`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L484) | 通过 Instruments 截取主屏 PNG | 部分 | [`TakeScreenshotAsync`](../iMobileDevice.Managed/Dvt/DvtClient.cs) 返回 PNG，当前覆盖 17.4+ USB 路径，旧版 DVT 传输待补 | P4 |
| [`developer dvt vm-regions`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L499) | 列可调试应用的虚拟内存区域 | 待实现 | — | P4 |
| [`developer dvt memgraph`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L511) | 导出应用内存图 | 待实现 | — | P4 |
| [`developer dvt dyld-metrics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L536) | 读取动态加载指标 | 待实现 | — | P4 |
| [`developer dvt allocations`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L593) | 采集应用内存分配 | 待实现 | — | P4 |
| [`developer dvt xcuitest`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L656) | 启动 XCTest Runner | 待实现 | — | P5 |
| [`developer dvt trace-codes`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L764) | 列内核跟踪代码 | 待实现 | — | P4 |
| [`developer dvt name-for-uid`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L772) | 通过 UID 查用户 | 待实现 | — | P4 |
| [`developer dvt name-for-gid`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L782) | 通过 GID 查用户组 | 待实现 | — | P4 |
| [`developer dvt oslog`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L792) | 订阅统一日志 | 待实现 | — | P4 |
| [`developer dvt energy`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L858) | 采集进程能耗 | 待实现 | — | P4 |
| [`developer dvt network-statistics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L880) | 采集进程网络流量 | 待实现 | — | P4 |
| [`developer dvt notifications`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L899) | 订阅内存和应用状态 | 待实现 | — | P4 |
| [`developer dvt graphics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L908) | 采集 GPU 指标 | 待实现 | — | P4 |
| [`developer dvt fps`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L917) | 采集显示帧率 | 待实现 | — | P4 |
| [`developer dvt har`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L926) | 启用 HTTP 归档日志 | 待实现 | — | P4 |
| [`developer dvt sysmon system`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/sysmon/__init__.py#L26) | 采集系统指标快照 | 待实现 | — | P4 |
| [`developer dvt sysmon process single`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/sysmon/process.py#L416) | 采集进程指标快照 | 待实现 | — | P4 |
| [`developer dvt sysmon process monitor threshold`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/sysmon/process.py#L468) | 按 CPU 阈值监控 | 待实现 | — | P4 |
| [`developer dvt sysmon process monitor process`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/sysmon/process.py#L485) | 按筛选条件跟踪单个进程 | 待实现 | — | P4 |
| [`developer dvt core-profile-session live`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/core_profile_session.py#L295) | 接收内核跟踪事件 | 待实现 | — | P4 |
| [`developer dvt core-profile-session save`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/core_profile_session.py#L367) | 保存原始跟踪 | 待实现 | — | P4 |
| [`developer dvt core-profile-session stackshot`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/core_profile_session.py#L556) | 读取线程调用栈快照 | 待实现 | — | P4 |
| [`developer dvt core-profile-session parse-live`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/core_profile_session.py#L713) | 解析实时跟踪 | 待实现 | — | P4 |
| [`developer dvt core-profile-session events`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/core_profile_session.py#L941) | 列出可解析的跟踪事件 | 待实现 | — | P4 |
| [`developer dvt core-profile-session callstacks-live`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/core_profile_session.py#L1024) | 接收采样调用栈 | 待实现 | — | P4 |
| [`developer dvt simulate-location clear`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/simulate_location.py#L20) | 清除模拟位置 | 待实现 | — | P4 |
| [`developer dvt simulate-location set`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/simulate_location.py#L28) | 设定模拟位置 | 待实现 | — | P4 |
| [`developer dvt simulate-location play`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/simulate_location.py#L47) | 回放 GPX 路线 | 待实现 | — | P4 |
| [`developer dvt condition list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L120) | 列环境模拟配置 | 待实现 | — | P4 |
| [`developer dvt condition clear`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L128) | 清除模拟条件 | 待实现 | — | P4 |
| [`developer dvt condition set`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/dvt/__init__.py#L136) | 应用模拟条件 | 待实现 | — | P4 |
| [`developer fetch-symbols list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/fetch_symbols.py#L32) | 列可下载的符号 | 待实现 | — | P4 |
| [`developer fetch-symbols download`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/fetch_symbols.py#L123) | 下载设备符号 | 待实现 | — | P4 |
| [`developer simulate-location clear`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/simulate_location.py#L23) | 清除旧版位置模拟 | 待实现 | — | P4 |
| [`developer simulate-location set`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/simulate_location.py#L30) | 设置旧版模拟位置 | 待实现 | — | P4 |
| [`developer simulate-location play`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/simulate_location.py#L45) | 回放旧版 GPX 路线 | 待实现 | — | P4 |
| [`developer accessibility run-audit`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L90) | 运行无障碍审核 | 待实现 | — | P5 |
| [`developer accessibility supported-audit-types`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L103) | 查询支持的审核类型 | 待实现 | — | P5 |
| [`developer accessibility capabilities`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L110) | 查询服务能力 | 待实现 | — | P5 |
| [`developer accessibility shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L116) | 打开无障碍服务会话 | 待实现 | — | P5 |
| [`developer accessibility notifications`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L123) | 订阅无障碍事件 | 待实现 | — | P5 |
| [`developer accessibility list-items`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L142) | 枚举界面元素 | 待实现 | — | P5 |
| [`developer accessibility element-attributes`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/__init__.py#L158) | 读取元素属性 | 待实现 | — | P5 |
| [`developer accessibility settings show`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/settings.py#L23) | 查看辅助功能设置 | 待实现 | — | P5 |
| [`developer accessibility settings set`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/settings.py#L31) | 写入辅助功能设置 | 待实现 | — | P5 |
| [`developer accessibility settings reset`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/accessibility/settings.py#L48) | 重置辅助功能设置 | 待实现 | — | P5 |
| [`developer debugserver applist`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/debugserver.py#L56) | 列出可调试应用 | 待实现 | — | P4 |
| [`developer debugserver start-server`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/debugserver.py#L63) | 启动调试服务 | 待实现 | — | P4 |
| [`developer debugserver lldb`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/debugserver.py#L113) | 接入 LLDB | 待实现 | — | P4 |
| [`developer arbitration version`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/arbitration.py#L23) | 查询仲裁协议版本 | 待实现 | — | P4 |
| [`developer arbitration check-in`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/arbitration.py#L31) | 申请设备占用 | 待实现 | — | P4 |
| [`developer arbitration check-out`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/arbitration.py#L55) | 释放设备占用 | 待实现 | — | P4 |
| [`developer core-device list-directory`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L100) | 列文件域目录 | 待实现 | — | P4 |
| [`developer core-device read-file`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L128) | 从文件域读取文件 | 待实现 | — | P4 |
| [`developer core-device propose-empty-file`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L146) | 以指定属性创建空文件 | 待实现 | — | P4 |
| [`developer core-device create-directory`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L177) | 创建目录 | 待实现 | — | P4 |
| [`developer core-device remove-directory`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L190) | 移除空目录 | 待实现 | — | P4 |
| [`developer core-device remove-file`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L203) | 删除文件或链接 | 待实现 | — | P4 |
| [`developer core-device rename`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L216) | 移动或改名 | 待实现 | — | P4 |
| [`developer core-device create-symlink`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L230) | 创建符号链接 | 待实现 | — | P4 |
| [`developer core-device stat`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L259) | 读取文件属性 | 待实现 | — | P4 |
| [`developer core-device write-file`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L273) | 写入文件 | 待实现 | — | P4 |
| [`developer core-device watch`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L287) | 监听文件变化（iOS 27+） | 待实现 | — | P4 |
| [`developer core-device launch-application`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L303) | 启动应用 | 待实现 | — | P4 |
| [`developer core-device list-processes`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L348) | 列进程 | 待实现 | — | P4 |
| [`developer core-device uninstall`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L356) | 卸载应用 | 待实现 | — | P4 |
| [`developer core-device send-signal-to-process`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L367) | 发送进程信号 | 待实现 | — | P4 |
| [`developer core-device get-device-info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L379) | 查询设备信息 | 待实现 | — | P4 |
| [`developer core-device get-display-info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L387) | 查询显示信息 | 待实现 | — | P4 |
| [`developer core-device monitor-display-info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L395) | 订阅显示变化 | 待实现 | — | P4 |
| [`developer core-device query-mobilegestalt`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L404) | 查询 MobileGestalt 属性 | 待实现 | — | P4 |
| [`developer core-device get-lockstate`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L415) | 读取锁定状态 | 待实现 | — | P4 |
| [`developer core-device paste`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L423) | 读剪贴板 | 待实现 | — | P5 |
| [`developer core-device copy`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L442) | 写剪贴板 | 待实现 | — | P5 |
| [`developer core-device rotate`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L467) | 旋转设备 | 待实现 | — | P4 |
| [`developer core-device user-interface-style`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L481) | 读取或设置界面风格 | 待实现 | — | P4 |
| [`developer core-device list-apps`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L497) | 枚举应用 | 待实现 | — | P4 |
| [`developer core-device fetch-app-icon`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L505) | 下载应用图标 | 待实现 | — | P4 |
| [`developer core-device stream-processes`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L534) | 流式读取进程列表 | 待实现 | — | P4 |
| [`developer core-device sysdiagnose`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L542) | 生成并下载诊断归档 | 待实现 | — | P4 |
| [`developer core-device screen-capture screenshot`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L597) | 通过 CoreDevice 截屏 | 待实现 | — | P5 |
| [`developer core-device hid button`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L714) | 触发具名硬件按键 | 待实现 | — | P5 |
| [`developer core-device hid raw-button`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L733) | 发送原始按键事件 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service list-connected`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L760) | 列虚拟 HID 服务 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service send-report`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L768) | 发送 HID 报告 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service tap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L866) | 单点触摸 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service drag`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L888) | 单指拖动 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service multi-tap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L924) | 多点触摸 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service multi-drag`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L941) | 多指拖动 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service swipe`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L968) | 移动指针 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service session`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L988) | 执行连续手势 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service type`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1057) | 输入文本 | 待实现 | — | P5 |
| [`developer core-device universal-hid-service key`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1127) | 发送键盘按键 | 待实现 | — | P5 |
| [`developer core-device display get-media-support-info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1168) | 查询媒体能力 | 待实现 | — | P5 |
| [`developer core-device display get-media-stream-server-status`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1176) | 查询流媒体服务状态 | 待实现 | — | P5 |
| [`developer core-device display start-video-stream`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1184) | 采集视频流 | 待实现 | — | P5 |
| [`developer core-device display start-audio-stream`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1209) | 采集音频流 | 待实现 | — | P5 |
| [`developer core-device display serve-web`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1234) | 提供浏览器投屏与控制（iOS 27+） | 待实现 | — | P5 |
| [`developer core-device display serve-vnc`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1383) | 提供 VNC 投屏（iOS 27+、macOS 宿主） | 待实现 | — | P5 |
| [`developer core-device location available-scenarios`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/core_device.py#L1488) | 列内置的位置模拟场景 | 待实现 | — | P4 |
| [`developer wda launch`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L139) | 启动应用会话 | 待实现 | — | P5 |
| [`developer wda tap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L152) | 点击元素 | 待实现 | — | P5 |
| [`developer wda press`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L192) | 触发设备按键 | 待实现 | — | P5 |
| [`developer wda unlock`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L222) | 调用 WDA 解锁操作 | 待实现 | — | P5 |
| [`developer wda list-items`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L241) | 列可点击元素 | 待实现 | — | P5 |
| [`developer wda screenshot`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L360) | 通过 WDA 截屏 | 待实现 | — | P5 |
| [`developer wda status`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L381) | 查询 Runner 状态 | 待实现 | — | P5 |
| [`developer wda type`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L392) | 输入文本 | 待实现 | — | P5 |
| [`developer wda swipe`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L423) | 滑动 | 待实现 | — | P5 |
| [`developer wda window-size`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/developer/wda.py#L465) | 读取窗口尺寸 | 待实现 | — | P5 |

## diagnostics · 设备诊断

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`diagnostics restart`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/__init__.py#L44) | 重新启动 | 待实现 | — | P2 |
| [`diagnostics shutdown`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/__init__.py#L66) | 关机 | 待实现 | — | P2 |
| [`diagnostics sleep`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/__init__.py#L73) | 休眠 | 待实现 | — | P2 |
| [`diagnostics info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/__init__.py#L80) | 读诊断信息 | 待实现 | — | P2 |
| [`diagnostics ioregistry`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/__init__.py#L87) | 查 IORegistry | 待实现 | — | P2 |
| [`diagnostics mg`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/__init__.py#L101) | 查 MobileGestalt | 待实现 | — | P2 |
| [`diagnostics battery single`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/battery.py#L21) | 电池数据快照 | 待实现 | — | P2 |
| [`diagnostics battery monitor`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/battery.py#L29) | 持续采集电池数据 | 待实现 | — | P2 |
| [`diagnostics battery wifi`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/diagnostics/battery.py#L53) | 读取 Wi-Fi 接口的 IORegistry 数据 | 待实现 | — | P2 |

## doctor · 宿主诊断

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`doctor`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/doctor.py#L19) | 诊断主机传输能力 | 部分 | [`DeviceConnectionDiagnostics.InspectAsync`](../iMobileDevice.Managed/Diagnostics/DeviceConnectionDiagnostics.cs) 检查 usbmuxd、匹配设备及 Lockdown 配对会话。Bonjour、RSD 隧道、开发者环境诊断待补 | P1 |

## lockdown · 设备属性与配对

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`lockdown recovery`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L42) | 进入恢复模式 | 待实现 | — | P6 |
| [`lockdown service`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L52) | 连接普通服务并打开交互会话 | 部分 | [`ConnectToServiceAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可返回服务流，缺交互会话 | P1 |
| [`lockdown developer-service`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L67) | 连接开发者服务并打开交互会话 | 部分 | [`ConnectToServiceAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可返回服务流，缺交互会话 | P1 |
| [`lockdown info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L82) | 读取全部设备属性 | 已有 | [`GetAllValuesAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) | P1 |
| [`lockdown get`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L89) | 按域或键读取属性 | 已有 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) | P1 |
| [`lockdown set`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L102) | 写属性 | 待实现 | — | P1 |
| [`lockdown remove`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L118) | 删除属性 | 待实现 | — | P1 |
| [`lockdown unpair`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L129) | 解除主机配对 | 部分 | [`DeletePairRecordAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs) 只清理主机记录，缺设备端 Unpair | P1 |
| [`lockdown pair`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L144) | 配对并记录设备信任 | 已有 | [`PairAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可等待设备信任并保存记录 | P1 |
| [`lockdown pair-wireless`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L213) | 无线配对 | 待实现 | — | P1 |
| [`lockdown pair-supervised`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L233) | 通过监督身份配对 | 待实现 | — | P1 |
| [`lockdown save-pair-record`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L250) | 导出配对记录 | 部分 | [`ReadPairRecordAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs) 和 [`ToPlist`](../iMobileDevice.Managed/Lockdown/PairRecord.cs) 可序列化已建模的字段，缺完整记录文件导出 | P1 |
| [`lockdown date`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L263) | 读取设备时间 | 部分 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可读时间戳，缺设备时间接口 | P1 |
| [`lockdown heartbeat`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L271) | 保持心跳 | 待实现 | — | P1 |
| [`lockdown language`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L278) | 读写语言 | 部分 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可读属性，缺相应设置接口 | P1 |
| [`lockdown locale`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L292) | 读写区域 | 部分 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可读属性，缺相应设置接口 | P1 |
| [`lockdown device-name`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L304) | 读写设备名 | 部分 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可读属性，缺相应设置接口 | P1 |
| [`lockdown wifi-connections`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L317) | 读写 Wi-Fi 连接开关 | 部分 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可读属性，缺相应设置接口 | P1 |
| [`lockdown start-tunnel`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L344) | 启动 Lockdown 隧道 | 部分 | [`CoreDeviceProxyClient`](../iMobileDevice.Managed/Tunnel/CoreDeviceProxyClient.cs) 提供 17.4+ 进程内路径，缺供外部进程使用的隧道 | P1 |
| [`lockdown remotepairing`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L373) | 读取 RemotePairing 握手信息，可选择配对 | 待实现 | — | P1 |
| [`lockdown assistive-touch`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L418) | 读写辅助触控设置 | 部分 | [`GetValueAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 可读属性，缺相应设置接口 | P1 |
| [`lockdown auto-answer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/lockdown.py#L434) | 设置来电自动接听 | 待实现 | — | P1 |

## mobilesync · 同步记录

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`mobilesync dump`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mobilesync.py#L36) | 导出同步数据 | 待实现 | — | P6 |
| [`mobilesync push`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mobilesync.py#L47) | 写入同步数据 | 待实现 | — | P6 |
| [`mobilesync delete`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mobilesync.py#L70) | 删除同步数据 | 待实现 | — | P6 |

## mounter · 镜像挂载

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`mounter list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L56) | 列已挂载的镜像 | 待实现 | — | P4 |
| [`mounter lookup`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L73) | 查镜像状态 | 待实现 | — | P4 |
| [`mounter umount-developer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L91) | 卸载传统开发者镜像 | 待实现 | — | P4 |
| [`mounter umount-personalized`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L104) | 卸载个性化镜像 | 待实现 | — | P4 |
| [`mounter mount-developer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L121) | 挂载传统镜像 | 待实现 | — | P4 |
| [`mounter mount-personalized`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L154) | 挂载个性化镜像 | 待实现 | — | P4 |
| [`mounter auto-mount`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L197) | 自动选择并挂载镜像 | 待实现 | — | P4 |
| [`mounter query-developer-mode-status`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L284) | 读取开发者模式状态 | 待实现 | — | P4 |
| [`mounter query-nonce`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L292) | 查询 nonce | 待实现 | — | P4 |
| [`mounter query-personalization-identifiers`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L309) | 查询个性化标识 | 待实现 | — | P4 |
| [`mounter query-personalization-manifest`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L317) | 查询个性化清单 | 待实现 | — | P4 |
| [`mounter roll-personalization-nonce`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L330) | 更新个性化 nonce | 待实现 | — | P4 |
| [`mounter roll-cryptex-nonce`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/mounter.py#L338) | 更新 Cryptex nonce | 待实现 | — | P4 |

## notification · 系统通知

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`notification post`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/notification.py#L60) | 发布通知 | 待实现 | — | P2 |
| [`notification observe`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/notification.py#L75) | 订阅指定通知 | 待实现 | — | P2 |
| [`notification observe-all`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/notification.py#L93) | 订阅全部支持的通知 | 待实现 | — | P2 |
| [`notification get-state`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/notification.py#L110) | 读取 Darwin 通知状态（iOS 27.2+） | 待实现 | — | P2 |
| [`notification set-state`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/notification.py#L121) | 写入并保持 Darwin 通知状态（iOS 27.2+） | 待实现 | — | P2 |

## pcap · 网络抓包

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`pcap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/pcap.py#L47) | 采集设备网络包，可保存为 pcapng | 待实现 | — | P5 |

## power-assertion · 电源管理

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`power-assertion`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/power_assertion.py#L18) | 持有设备电源断言 | 待实现 | — | P6 |

## processes · 进程

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`processes ps`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/processes.py#L18) | 列出进程 | 待实现 | — | P2 |
| [`processes pgrep`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/processes.py#L25) | 按名称检索进程 | 待实现 | — | P2 |

## profile · 配置与监督

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`profile list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L27) | 列配置描述文件 | 待实现 | — | P2 |
| [`profile install`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L34) | 安装描述文件 | 待实现 | — | P2 |
| [`profile cloud-configuration`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L64) | 查询或写入云监督配置 | 待实现 | — | P2 |
| [`profile download-cloud-configuration`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L83) | 让设备下载并应用 DEP 配置 | 待实现 | — | P2 |
| [`profile store`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L103) | 存储描述文件 | 待实现 | — | P2 |
| [`profile remove`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L116) | 移除描述文件 | 待实现 | — | P2 |
| [`profile set-wifi-power`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L126) | 切换 Wi-Fi 电源 | 待实现 | — | P2 |
| [`profile erase-device`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L136) | 通过配置服务抹除设备 | 待实现 | — | P2 |
| [`profile create-keybag`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L161) | 创建监督密钥材料 | 待实现 | — | P2 |
| [`profile supervise`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L182) | 应用监督配置 | 待实现 | — | P2 |
| [`profile install-wifi-profile`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L219) | 安装 Wi-Fi 配置 | 待实现 | — | P2 |
| [`profile install-http-proxy`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L245) | 安装 HTTP 代理配置 | 待实现 | — | P2 |
| [`profile remove-http-proxy`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L271) | 移除代理配置 | 待实现 | — | P2 |
| [`profile install-restrictions-profile`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L278) | 安装限制描述文件 | 待实现 | — | P2 |
| [`profile request-unlock-token`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L316) | 请求解锁令牌 | 待实现 | — | P2 |
| [`profile clear-passcode`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L337) | 执行管理端清除口令操作 | 待实现 | — | P2 |
| [`profile clear-restrictions-password`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L361) | 清除限制口令 | 待实现 | — | P2 |
| [`profile security-info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L375) | 读取安全信息 | 待实现 | — | P2 |
| [`profile set-wallpaper`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/profile.py#L389) | 设置墙纸 | 待实现 | — | P2 |

## provision · 签名描述文件

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`provision install`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/provision.py#L24) | 安装签名描述文件 | 待实现 | — | P2 |
| [`provision remove`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/provision.py#L35) | 移除指定文件 | 待实现 | — | P2 |
| [`provision clear`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/provision.py#L45) | 清理全部文件 | 待实现 | — | P2 |
| [`provision list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/provision.py#L53) | 列签名描述文件 | 待实现 | — | P2 |
| [`provision dump`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/provision.py#L60) | 导出签名描述文件 | 待实现 | — | P2 |

## remote · 远程服务与隧道

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`remote tunneld`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L102) | 运行共享隧道服务 | 待实现 | — | P1 |
| [`remote browse`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L179) | 发现远程设备 | 待实现 | — | P1 |
| [`remote rsd-info`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L208) | 读取 RSD 属性和服务表 | 部分 | [`PeerProperties / Services`](../iMobileDevice.Managed/RemoteXpc/RemoteServiceDiscoveryClient.cs) 解析属性与服务表，现仅有用户态隧道入口 | P1 |
| [`remote auxiliary-metadata`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L214) | 读取附加元数据 | 待实现 | — | P1 |
| [`remote start-tunnel`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L349) | 建立远程隧道 | 待实现 | — | P1 |
| [`remote pair`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L508) | 远程配对 | 待实现 | — | P1 |
| [`remote pair-host`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L564) | 广播本机并接受设备发起的配对（iOS 27+） | 待实现 | — | P1 |
| [`remote delete-pair`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L639) | 清理远程配对记录 | 待实现 | — | P1 |
| [`remote service`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/remote.py#L651) | 连接 RemoteXPC 服务并打开会话 | 部分 | [`ConnectToServiceAsync`](../iMobileDevice.Managed/RemoteXpc/RemoteServiceDiscoveryClient.cs) 和 [`ConnectAsync`](../iMobileDevice.Managed/RemoteXpc/RemoteXpcConnection.cs)，缺交互会话 | P1 |

## restore · 固件更新与恢复

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`restore enter-dfu`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L548) | 引导进入 DFU | 待实现 | — | P6 |
| [`restore shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L554) | 打开 Recovery/DFU 的 iBoot 会话 | 待实现 | — | P6 |
| [`restore enter`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L568) | 进入恢复模式 | 待实现 | — | P6 |
| [`restore exit`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L576) | 退出恢复模式 | 待实现 | — | P6 |
| [`restore restart`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L630) | 重启设备 | 待实现 | — | P6 |
| [`restore tss`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L678) | 请求固件签名票据 | 待实现 | — | P6 |
| [`restore preflight`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L694) | 读取恢复预检信息 | 待实现 | — | P6 |
| [`restore preflight-requests`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L714) | 取得预检请求 | 待实现 | — | P6 |
| [`restore ramdisk`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L739) | 启动恢复 RAM disk | 待实现 | — | P6 |
| [`restore update`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/restore.py#L747) | 更新固件或抹除恢复 | 待实现 | — | P6 |

## springboard · 主屏

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`springboard shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L41) | SpringBoard 交互会话 | 待实现 | — | P2 |
| [`springboard icon`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L54) | 导出应用图标 | 待实现 | — | P2 |
| [`springboard orientation`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L65) | 读取界面方向 | 待实现 | — | P2 |
| [`springboard wallpaper-home-screen`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L72) | 读取主屏墙纸 | 待实现 | — | P2 |
| [`springboard wallpaper-preview-image`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L88) | 读取墙纸预览 | 待实现 | — | P2 |
| [`springboard homescreen-icon-metrics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L110) | 读取主屏图标布局指标 | 待实现 | — | P2 |
| [`springboard state get`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/springboard.py#L35) | 读取当前图标布局 | 待实现 | — | P2 |

## syslog · 日志

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`syslog live-old`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/syslog.py#L72) | 接收传统 syslog | 待实现 | — | P2 |
| [`syslog live`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/syslog.py#L309) | 接收统一日志 | 待实现 | — | P2 |
| [`syslog collect`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/syslog.py#L475) | 导出日志归档 | 待实现 | — | P2 |

## usbmux · USB 连接

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`usbmux forward`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/usbmux.py#L26) | 本地端口监听及双向转发 | 部分 | [`ConnectAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs) 可打开设备端口，缺本地监听及转发循环 | P1 |
| [`usbmux list`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/usbmux.py#L80) | 枚举 USB 和网络设备 | 已有 | [`ListDevicesAsync / ListDevicesByConnectionTypeAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs) 列出 daemon 已发现的连接；[`InspectAsync`](../iMobileDevice.Managed/Diagnostics/DeviceConnectionDiagnostics.cs) 提供每条连接的设备摘要和独立诊断结果 | P1 |

## webinspector · 浏览器和 WebView

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`webinspector opened-tabs`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/webinspector.py#L220) | 列 Safari/WebView 页面 | 待实现 | — | P5 |
| [`webinspector launch`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/webinspector.py#L245) | 通过自动化会话在 Safari 打开 URL | 待实现 | — | P5 |
| [`webinspector shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/webinspector.py#L305) | 打开可驱动 Safari 的交互式会话 | 待实现 | — | P5 |
| [`webinspector js-shell`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/webinspector.py#L351) | 执行页面 JavaScript | 待实现 | — | P5 |
| [`webinspector cdp`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/webinspector.py#L424) | 提供 CDP 桥接 | 待实现 | — | P5 |

## idam · 设备音频与 MIDI

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`idam configuration-inquiry`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/idam.py#L26) | 查询设备音频/MIDI 配置 | 待实现 | — | P6 |
| [`idam enable`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/idam.py#L34) | 启用连接 | 待实现 | — | P6 |
| [`idam disable`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/idam.py#L42) | 禁用连接 | 待实现 | — | P6 |

## version · 工具版本

| 上游操作及源码 | 用户操作 | Managed | C# 入口或已知边界 | 阶段 |
| --- | --- | --- | --- | --- |
| [`version`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/cli/version.py#L7) | 查询已安装的工具版本 | 已有 | 读取包版本或程序集的 `AssemblyInformationalVersionAttribute` | P0 |
