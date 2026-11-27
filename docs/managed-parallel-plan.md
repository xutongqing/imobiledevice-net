# Managed 并行开发与合并计划

以 2026-11-27 的 `origin/main@7b88829` 为功能盘点起点。当日核对的 pymobiledevice3 默认分支 HEAD 与仓库固定基线均为 `9d54c30`。下面按可交付的用户操作拆分，每个 agent 从最新 `origin/main` 建自己的工作树和分支，PR 的目标选 `main`。本仓库的默认分支当前是 `master`。[上游基线](https://github.com/doronz88/pymobiledevice3/tree/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2) · [逐项功能](managed-parity.md) · [SDK 与协议](managed-api-parity.md)

## 还有多少工作

| 阶段 | 已有 | 部分 | 待实现 | 剩余工作重点 |
| --- | ---: | ---: | ---: | --- |
| P0 基线与打包 | 1 | 0 | 0 | 独立包 CI 已通过。Linux、macOS 的真机包消费和恢复模式宿主探测仍待验证 |
| P1 连接 | 4 | 15 | 18 | 已有 USB 选机与诊断。补 Bonjour、Wi-Fi 直连、断线重连、旧版本与共享隧道，同时验证双真机枚举 |
| P2 常用服务 | 9 | 2 | 62 | AFC 递归传输、应用查询与共享目录已在 Windows 联验。待补应用安装卸载、崩溃报告、日志、设备诊断、通知、主屏和描述文件 |
| P3 备份 | 5 | 3 | 2 | 选择规则及清单裁剪、恢复的副本选项、旧加密清单兼容性、iCloud 设置与抹除。Windows 加密全量与增量已通过，恢复已通过模拟测试，真机恢复待验收 |
| P4 开发者服务 | 0 | 2 | 99 | DTX 主动消息、进程与性能采样、开发者镜像、旧版截屏及各类开发者服务 |
| P5 自动化、Web 与媒体 | 0 | 0 | 48 | XCTest/WDA、WebInspector、媒体采集、投屏和输入。各服务按自己的传输与系统版本条件排期 |
| P6 固件及其他 | 0 | 0 | 33 | Recovery/DFU、固件更新与恢复、设备激活、Companion、MobileSync 等 |
| **303 个命令操作** | **19** | **22** | **262** | **284 项仍有功能差距** |

另有 114 行 SDK 与协议映射，含 74 个上游文档符号和 40 个补充源码入口，其中 2 项已有、20 项部分、92 项待实现。API 和命令分别统计。表中“已有”表示有公开 C# 入口，真机覆盖按平台另记。[Windows 已验证项](managed-windows-validation.md) · [P3 真机验收](managed-backup.md)

## 首批任务卡

任务 A、B 已完成 Windows 真机联验，C 独立推进。各任务书保留原交付边界和功能分支。需要同时推进 P3 时可再分配 D。[A、B 联合验收记录](managed-apps-afc.md)

- [发给 A 的任务书 · AFC 递归文件传输](agent-a-afc-transfer.md)
- [发给 B 的任务书 · 应用查询与 House Arrest 容器](agent-b-apps-house-arrest.md)
- [发给 C 的任务书 · Bonjour 局域网发现](agent-c-bonjour.md)

| 任务卡与分支 | 首个 PR 交付与代码范围 | 可以验收的结果 |
| --- | --- | --- |
| **A · AFC 递归传输** <br>`codex/p2-afc-transfer` | 在 `iMobileDevice.Managed/Afc/` 基于现有 `AfcClient` 实现 `afc ls --recursive`、`pull`、`push`。提供可用于媒体和应用容器的同一套传输入口，支持流式复制、取消、进度和 pull 的逐文件容错。 | 从普通 .NET 调用方往返包含空目录、空文件和多块文件的目录树，校验路径、长度、SHA-256。回归覆盖途中取消、文件错误和本地路径边界。 |
| **B · 应用查询和文件容器** <br>`codex/p2-apps-house-arrest` | 在 `iMobileDevice.Managed/Apps/` 提供 Installation Proxy 的应用枚举及按 Bundle ID 查询，覆盖类型筛选、大小及占位应用选项。通过 House Arrest 打开 Documents 或许可的容器，返回已有的 `AfcClient(Stream)` 对应会话。 | 模拟分批返回应用、无匹配、服务拒绝、容器切换到 AFC；Windows 真机与上游比较相同查询，并列举允许共享的测试应用目录。A 与 B 合并后再联验 `apps pull/push`。 |
| **C · Bonjour 网络发现** <br>`codex/p1-bonjour` | 在 `iMobileDevice.Managed/Bonjour/` 实现局域网发现，首个 PR 交付 `bonjour remotepairing` 和 `remotepairing-manual-pairing` 的地址、端口与可用名称。为 mobdev2 输出发现端点；后续 PR 再接已有配对身份和 Wi-Fi Lockdown，另验 RSD 信息查询。 | 用 DNS-SD 样本及可控响应验证 IPv4、带作用域的 IPv6、多接口重复广告、超时和取消。Windows 同网条件下对比上游发现结果；缺对应广播时登记设备条件。仅收到 mobdev2 广播时，该命令仍算部分覆盖。 |
| **D · P3 选择性备份（可选）** <br>`codex/p3-backup-selection` | 在 `iMobileDevice.Managed/Backup/` 增加可选的选择规则，先交付 `backup2 backup --only` 与 `--only-regex` 的匹配语义。保留完整备份默认行为，记录筛选快照的可用操作。清单裁剪作为后续独立 PR，分别验证明文与加密 `Manifest.db`。 | 用合成快照对比上游的保留与丢弃集合，覆盖规则组合、元数据保留、中断重试，以及筛选产物的恢复和展开前置检查。Windows 已保存的真机全量备份留作验收依据。 |

A、B、C、D 分别在自己的目录和专属测试文件中工作。B 可直接使用现有 `AfcClient(Stream)` 开发容器会话。P3 的 `BackupInfo` 已有备份专用的 Browse 与图标读取；B 可参考协议，待 B、D 都合并后由主线程接回公共客户端，并回归应用元数据和备份。各 PR 报告自己影响的对照表行，主线程统一修改总数、NuGet 消费测试和验收记录。

## 合并次序与真机安排

1. **第一批**。A、B 的查询与容器会话、C 的发现，以及可选的 D 均可单独审查和合并。主线程逐个在最新 `main` 上跑 Managed 回归；A、B 就位后，用一个只引用独立 NuGet 的程序串起“查询应用 → 打开 Documents → 上传并下载目录 → 比较 SHA-256”。
2. **复用已有的 P4 分支**。远端 `codex/managed-process-monitor` 的 `dc2fd74`、`d4b7ce2` 已有进程枚举、CPU/内存采样及按 Bundle ID 跨 PID 跟踪。主线程将这两个提交在最新 `main` 上适配。预合并已定位到 `Dvt/DtxConnection.cs` 和 `iMobileDevice.Managed.csproj` 两处冲突，还需核对旧工作流的打包步骤。回归覆盖主动消息与请求交错、断线唤醒、已到达的回复先于 EOF 交付、连续采样和截屏，再按实际入口更新 P4。[已有实现](https://github.com/xutongqing/imobiledevice-net/tree/codex/managed-process-monitor)
3. **第二批**。A 合并后可独立做 Crash Reports 列举与拉取；B 合并后可做应用安装与卸载。普通 Lockdown 的设备诊断、通知、SpringBoard 和日志可按服务另开 PR。C 后续做 Wi-Fi 会话、按 UDID 重连；旧隧道和 DDI 按系统版本分别验收。P5 的 WebInspector 可独立排，依赖开发者服务的功能按实际使用的 DTX、RSD 或 DDI 入口排。P6 的固件与恢复模式单独使用专用设备。
4. **Windows 真机和发布**。模拟协议测试可同时运行；当前同一台 iPhone 的真机操作由主线程排队，记录包版本、系统版本和通过的操作。普通 AFC 写入使用自建测试目录；真机恢复需另外指定可覆盖的设备。最终以合并提交的 GitHub Actions 结果及其独立 Managed NuGet 做集成验收。macOS 真机留到设备就绪，CI 状态单独记录。
