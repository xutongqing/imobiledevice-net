# iMobileDevice.Managed 实施路线图

目标是让 .NET 业务项目通过 `iMobileDevice.Managed` 完成基线版本 pymobiledevice3 的设备操作。设备协议和业务流程用 C# 实现，产品运行时只需 .NET 和对应功能的宿主组件。Python 仅用于开发期间的行为对照。第一版基线固定在 2026-11-26 的上游提交 `9d54c30`，Managed 起点为 `fc78d8a`。[逐项功能对照](managed-parity.md) · [上游基线](https://github.com/doronz88/pymobiledevice3/tree/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2)

## 平台和设备前提

下表是计划要对齐的上游条件。每条 Managed 路径的实际进度和验证记录见功能对照表。

| 场景 | Windows | Linux | macOS |
| --- | --- | --- | --- |
| 普通 USB、配对、AFC、备份 | 安装桌面版 iTunes 提供的驱动和 Apple Mobile Device Service。客户端默认连接本机 27015 端口 | 安装并启动 usbmuxd，允许服务访问 USB 设备 | 系统内置 usbmuxd |
| Wi-Fi | 分别验收桌面版 iTunes 和商店版 Apple Devices。后者需要显式网络发现来连接拔线后的设备 | 验收配对后的 Bonjour 发现和直连 | 验收系统发现和直连 |
| iOS 17.4+ USB 开发者服务 | 进程内用户态隧道，普通用户权限 | 同左 | 同左，另可使用系统原生隧道 |
| iOS 17.0–17.3 开发者服务 | 上游使用隧道服务、管理员权限和第三方驱动 | 上游使用具有建隧道权限的 tunneld | 系统 remoted 原生隧道 |
| Recovery / DFU | 上游使用 libusb，验证恢复模式驱动和重新枚举 | 上游使用 libusb，验证设备节点权限 | 上游使用 IOKit，部分新设备还涉及 deviceinterfaced |

普通配对需在设备上确认信任。开发者模式和开发者镜像（DDI）条件随服务与系统版本变化，WDA 需要设备端已安装可运行的 Runner。恢复模式 USB 后端在 P0 做可行性验证，记录各平台需要的系统接口或可选组件，供 P6 定案。[安装说明](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/installation.md) · [隧道矩阵](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/guides/ios17-tunnels.md) · [恢复模式](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/guides/restore.md)

## 里程碑

| 阶段 | 交付内容 | 验收 |
| --- | --- | --- |
| P0 基线与独立包 | 逐项登记命令、公开服务、已有 C# 入口和缺口。独立构建并上传 Managed NuGet | 新建 .NET 8 项目，仅从本地源安装制品并运行。另用已安装包在三平台验证 USB 枚举、设备信息、AFC 和适用设备上的 DVT 截屏 |
| P1 连接与诊断 | 按 UDID 选机、多设备、配对状态、Wi-Fi/Bonjour、取消重连、环境诊断。为 RSD 增加旧系统隧道、共享隧道和 macOS 原生路径 | 在三平台区分服务缺失、未连接、未信任和锁屏，记录拔线、双机选取与重连结果 |
| P2 常用服务 | AFC 递归和流式传输、应用安装/枚举、House Arrest、诊断、通知、SpringBoard、描述文件、日志和崩溃报告 | 枚举已安装应用，导出其允许共享的文件，拉取崩溃报告，并验证传输进度与取消 |
| P3 备份与恢复备份 | MobileBackup2 主机端消息循环、全量/增量/加密备份、列举、提取、恢复和错误处理 | 与上游比较同设备的备份清单、内容哈希和恢复结果。覆盖空间不足、中断与再次运行 |
| P4 开发者服务 | 按服务检查开发者模式及 DDI，支持传统及个性化镜像。扩展 DTX 以分发设备主动消息，接入进程控制、采样、位置模拟、符号和调试服务 | 回归截屏；在同一会话采样、取消订阅、再次订阅。验证适用 iOS 上的镜像和进程操作 |
| P5 自动化、Web 与媒体 | XCTest/WDA、Safari/WebView 与 CDP、抓包、输入、投屏和音频 | 驱动已安装 Runner 操作测试应用，连接可检查的 WebView，验证持续抓包、媒体流及断开时的资源释放 |
| P6 固件与其余服务 | Recovery、DFU、IPSW、TSS、更新与抹除恢复；补齐激活、Companion、Cryptex、MobileSync、蓝牙日志、IDAM、WebDAV 等余项 | 在专用设备上验证更新与抹除恢复、模式切换和重连。逐行检查适用操作及验证记录 |

P2、P3 的普通 USB 路径可以在 P1 旧系统隧道兼容期间交付。开发者服务的首条扩展路径是 DTX 主动消息、进程列表和持续采样。

## P0 交付与验证

- [x] 固定上游提交，登记末级命令和公共服务。见[功能对照表](managed-parity.md)。
- [x] 增加独立的 `pack-managed` 作业。制品名为 `imobiledevice-managed-nupkg`，以隔离包源和空缓存验证普通业务项目能还原并运行。见[工作流](../.github/workflows/build-native-and-pack.yml)。
- [ ] 完成首轮 Windows、Linux、macOS 真机包消费报告，记录系统、机型、iOS、连接方式、包版本和结果。把真机测试未启用的情况在报告中显式标为跳过。
- [ ] 为环境诊断记录所需探测项，形成 P1 的首个实现任务；验证恢复模式 USB 后端的宿主条件。

## 维护与验收口径

每项对应一个用户操作。比较同一输入下的设备字段、输出文件、事件、失败类别、取消与断连行为。成功启动服务和得到通用字节流只计底层能力。真机覆盖按主机和 iOS 分开记录，至少包含 iOS 17 前、17.0–17.3 和 17.4+，实时投屏另取满足其版本要求的设备。更新上游基线时，先列出新增、改名、删除的操作，再更新 C# 映射与验证记录。
