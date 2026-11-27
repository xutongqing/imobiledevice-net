# pymobiledevice3 公开 API 与协议对照

上游基线 `9d54c30` 的 API 参考页明确列出 **74 个符号**，包含服务类、连接工厂和数据模型。本表逐个映射，另补 **40 个**与传输、纯 SDK 使用或后续里程碑有关的源码入口。方法列举该类型直接声明的代表性成员；完整接口见每行源码。命令的用户行为、覆盖口径和真机状态见[末级操作对照表](managed-parity.md)。[上游 API 参考目录](https://github.com/doronz88/pymobiledevice3/tree/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api)

文件归档 `FileRelayService`、时钟测量 `TimeSyncService`、预启动 `PreboardService` 等，即使没有独立的同名 CLI 命令，也有各自的移植项。服务与对应命令分别核算。

## 应用、文件与描述文件

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/apps-files.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`HouseArrestService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/house_arrest.py#L16) | 应用文件容器 | `create`、`send_command`、`shell` | 部分 | `AppContainer.OpenAsync` 通过 Lockdown 获取 AFC 会话；交互 Shell 和 RSD 待实现。[用法与验收](managed-apps-afc.md) | P2 |
| [`MobileConfigService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobile_config.py#L41) | 配置描述文件 | `hello`、`flush`、`escalate`、`get_stored_profile` | 待实现 | — | P2 |
| [`MisagentService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/misagent.py#L22) | 签名描述文件 | `install`、`remove`、`copy_all` | 待实现 | — | P2 |
| [`FileRelayService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/file_relay.py#L27) | 请求文件归档 | `stop_session`、`request_sources` | 待实现 | — | P6 |
| [`InstallCoordinationProxyService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/install_coordination_proxy.py#L70) | 查询应用安装记录 | `query` | 待实现 | — | P2 |
| [`InstallRecord`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/install_coordination_proxy.py#L61) | 安装记录模型 | — | 待实现 | — | P2 |

## 备份、符号与位置

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/backup-symbols.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`Mobilebackup2Service`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobilebackup2.py#L178) | 备份与恢复 | `get_will_encrypt`、`backup`、`restore`、`info` | 部分 | [MobileBackupClient](managed-backup.md) 提供备份、恢复、列举、提取和密码操作 | P3 |
| [`DtFetchSymbols`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dtfetchsymbols.py#L9) | 传统设备符号导出 | `list_files`、`get_file` | 待实现 | — | P4 |
| [`RemoteFetchSymbolsService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/remote_fetch_symbols.py#L28) | 远程设备符号下载 | `get_dsc_file_list`、`download` | 待实现 | — | P4 |
| [`DtSimulateLocation`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/simulate_location.py#L8) | 旧版位置模拟 | `clear`、`set` | 待实现 | — | P4 |

## 采集、日志与自动化

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/capture.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`ScreenshotService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/screenshot.py#L8) | 传统 screenshotr 截屏 | `take_screenshot` | 待实现 | — | P4 |
| [`PcapdService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/pcapd.py#L345) | 网络抓包 | `watch`、`write_to_pcap` | 待实现 | — | P5 |
| [`SyslogService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/syslog.py#L48) | 系统日志 | `watch` | 已有 | [`SyslogRelayClient.WatchLinesAsync`](../iMobileDevice.Managed/Services/SyslogRelayClient.cs)，流式解码 `com.apple.syslog_relay` 日志行 | P2 |
| [`DebugServerAppList`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/debugserver_applist.py#L10) | 列可调试应用 | `get` | 待实现 | — | P4 |
| [`AccessibilityAudit`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/accessibilityaudit.py#L322) | 无障碍审核与元素 | `shell`、`capabilities`、`run_audit`、`supported_audits_types` | 待实现 | — | P5 |
| [`WebinspectorService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/webinspector.py#L187) | Safari 和 WebView 检查 | `is_connected`、`automation_session`、`inspector_session`、`get_open_pages` | 待实现 | — | P5 |
| [`WdaServiceClient`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/wda.py#L304) | 设备服务上的 WDA | `start_session`、`find_element`、`click`、`press_button` | 待实现 | — | P5 |
| [`WdaClient`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/wda.py#L26) | 通过 HTTP 使用 WDA | `start_session`、`find_element`、`click`、`press_button` | 待实现 | — | P5 |

## 会话与连接

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/connection.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`create_using_usbmux`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L1610) | 由 usbmux 创建配对会话 | — | 部分 | [`ConnectByUdidAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 支持 UDID、连接类型及会话选项；自动重连待补 | P1 |
| [`create_using_mobdev2`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L1910) | 通过 Bonjour 建立会话 | — | 待实现 | — | P1 |
| [`LockdownClient`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L114) | 属性、配对与服务启动 | `create`、`product_version`、`product_build_version`、`device_class` | 部分 | [`LockdownClient`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs)，已有查询、配对和 TLS，缺写入和删除属性等 | P1 |
| [`UsbmuxLockdownClient`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L1217) | usbmux 上的 Lockdown | `short_info`、`fetch_pair_record`、`create_service_connection` | 部分 | [`ConnectByUdidAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs) 与 [`DeviceConnectionResult.Summary`](../iMobileDevice.Managed/Diagnostics/DeviceConnectionDiagnostics.cs) 已接入；配对库多来源和重试语义待补 | P1 |
| [`TcpLockdownClient`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L1345) | TCP 直连的 Lockdown | `pair`、`get_service_connection_attributes`、`create_service_connection` | 待实现 | — | P1 |
| [`get_mobdev2_lockdowns`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L1982) | 发现网络上的已配对设备 | — | 部分 | 已有端点发现，Wi-Fi Lockdown 会话待实现。[验收](managed-bonjour.md) | P1 |
| [`create_using_tcp`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/lockdown.py#L1723) | 按地址创建 TCP 会话 | — | 待实现 | — | P1 |
| [`RemoteServiceDiscoveryService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/remote_service_discovery.py#L86) | 读取 RSD 属性并打开服务 | `is_in_process_tunnel`、`get_auxiliary_metadata`、`is_remote_web_inspector_enabled`、`product_version` | 部分 | [`RemoteServiceDiscoveryClient`](../iMobileDevice.Managed/RemoteXpc/RemoteServiceDiscoveryClient.cs)，17.4+ USB 可读服务表并连端口，缺其他路径和部分服务握手 | P1 |
| [`PreferredRsdTunnel`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/rsd_tunnel.py#L28) | 选择合适的 RSD 隧道 | — | 部分 | [`UserspaceTcpTunnel`](../iMobileDevice.Managed/Tunnel/UserspaceTcpTunnel.cs)，覆盖 USB CoreDeviceProxy 路径，发现、自动选路与其他系统版本待补 | P1 |
| [`UserspaceRsdTunnel`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/userspace_tunnel.py#L858) | 用户态 RSD 隧道 | — | 部分 | [`UserspaceTcpTunnel`](../iMobileDevice.Managed/Tunnel/UserspaceTcpTunnel.cs)，覆盖 USB CoreDeviceProxy 路径，发现、自动选路与其他系统版本待补 | P1 |
| [`establish_userspace_rsd`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/userspace_tunnel.py#L1103) | 建立用户态 RSD 会话 | — | 部分 | [`UserspaceTcpTunnel`](../iMobileDevice.Managed/Tunnel/UserspaceTcpTunnel.cs)，覆盖 USB CoreDeviceProxy 路径，发现、自动选路与其他系统版本待补 | P1 |
| [`NativeRemotedTunnel`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/native_tunnel.py#L935) | 使用 macOS 系统隧道 | — | 待实现 | — | P1 |
| [`get_tunneld_devices`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/tunneld/api.py#L156) | 发现共享隧道设备 | — | 待实现 | — | P1 |

## Instruments / DVT

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/dvt.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`DvtProvider`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/dvt_provider.py#L8) | Instruments 服务提供者 | — | 部分 | [`DvtClient`](../iMobileDevice.Managed/Dvt/DvtClient.cs)，已有 17.4+ USB 通道、截屏、进程列表与主动消息流式订阅；旧系统开发者传输待补 | P4 |
| [`ProcessControl`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/process_control.py#L102) | 进程控制 | `signal`、`disable_memory_limit_for_pid`、`kill`、`is_debuggable` | 待实现 | — | P4 |
| [`DeviceInfo`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/device_info.py#L65) | 设备及进程查询 | `ls`、`execname_for_pid`、`proclist`、`is_running_pid` | 部分 | [`DvtClient.ListProcessesAsync`](../iMobileDevice.Managed/Dvt/DvtClient.cs) 与 `GetApplicationProcessIdAsync` 已支持运行进程列举与 Bundle ID 查 PID | P4 |
| [`ApplicationListing`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/application_listing.py#L16) | 应用清单 | `applist` | 待实现 | — | P4 |
| [`Screenshot`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/screenshot.py#L12) | Instruments 主屏 PNG | `get_screenshot` | 部分 | [`TakeScreenshotAsync`](../iMobileDevice.Managed/Dvt/DvtClient.cs)，已有 17.4+ USB PNG，旧系统开发者传输待补 | P4 |
| [`Sysmontap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/sysmontap.py#L10) | 进程和系统采样 | `create`、`iter_processes` | 部分 | [`DvtClient.WatchProcessesAsync`](../iMobileDevice.Managed/Dvt/DvtProcessMonitor.cs) 与 [`WatchApplicationAsync`](../iMobileDevice.Managed/Dvt/DvtApplicationMonitor.cs) 支持整机与单 App 实时 CPU/内存采样 | P4 |
| [`NetworkMonitor`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/network_monitor.py#L131) | 网络事件 | — | 待实现 | — | P4 |
| [`EnergyMonitor`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/energy_monitor.py#L22) | 能耗监控 | — | 待实现 | — | P4 |
| [`NetworkStatistics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/network_statistics.py#L24) | 网络统计 | — | 待实现 | — | P4 |
| [`Graphics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/graphics.py#L34) | GPU 和帧率 | `fps` | 待实现 | — | P4 |
| [`ActivityTraceTap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/activity_trace_tap.py#L346) | 活动跟踪 | — | 待实现 | — | P4 |
| [`CoreProfileSessionTap`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/core_profile_session_tap.py#L823) | 内核跟踪和线程栈 | `get_stackshot`、`dump`、`pump_kdbuf_chunks`、`get_kdbuf_stream` | 待实现 | — | P4 |
| [`Notifications`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/notifications.py#L34) | 运行时通知 | — | 待实现 | — | P4 |
| [`ConditionInducer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/condition_inducer.py#L25) | 模拟环境条件 | `list`、`set`、`clear` | 待实现 | — | P4 |
| [`LocationSimulation`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/location_simulation.py#L17) | 新系统位置模拟 | `set`、`clear` | 待实现 | — | P4 |

## 通用设备服务

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/services.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`LockdownService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/lockdown_service.py#L36) | 服务启动与连接生命周期 | `service` | 部分 | [`ConnectToServiceAsync`](../iMobileDevice.Managed/Lockdown/LockdownClient.cs)，可启动服务并返回流 | P1 |
| [`OsTraceService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/os_trace.py#L206) | 系统日志、归档和进程清单 | `get_pid_list`、`create_archive`、`collect`、`syslog` | 待实现 | — | P2 |
| [`InstallationProxyService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/installation_proxy.py#L67) | 安装卸载与查询应用 | `send_cmd_for_bundle_identifier`、`upgrade`、`restore`、`uninstall` | 部分 | `InstallationProxyClient` 可列举与查询；安装、升级和卸载待实现。[用法与验收](managed-apps-afc.md) | P2 |
| [`StreamingZipConduitService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/streaming_zip_conduit.py#L163) | 传输应用安装包 | `install` | 待实现 | — | P2 |
| [`AfcService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/afc.py#L321) | 文件目录读写与传输 | `wait_terminated`、`pull`、`exists`、`wait_exists` | 部分 | `AfcClient` 已支持目录递归、传输、存在性查询和删除；文件出现和 AFC 连接终止的等待接口待实现。[用法与验收](managed-apps-afc.md) | P2 |
| [`DiagnosticsService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/diagnostics.py#L1011) | 设备诊断与电源操作 | `mobilegestalt`、`action`、`restart`、`shutdown` | 已有 | [`DiagnosticsRelayClient`](../iMobileDevice.Managed/Services/DiagnosticsRelayClient.cs) 支持 IORegistry、电池、MobileGestalt、休眠、重启与关机 | P2 |
| [`BatteryInfo`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/diagnostics.py#L949) | 电池信息模型 | `from_ioregistry` | 已有 | [`BatteryInfo.FromIoRegistry`](../iMobileDevice.Managed/Services/DiagnosticsRelayClient.cs) | P2 |
| [`SpringBoardServicesService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/springboard.py#L18) | 主屏与图标 | `get_icon_state`、`set_icon_state`、`get_icon_pngdata`、`get_interface_orientation` | 部分 | [`SpringBoardServicesClient`](../iMobileDevice.Managed/Services/SpringBoardServicesClient.cs) 已支持图标 PNG、壁纸 PNG、屏幕方向与读取布局；写入布局待补 | P2 |
| [`CrashReportsManager`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/crash_reports.py#L99) | 崩溃报告管理 | `clear`、`ls`、`parse`、`parse_latest` | 部分 | [`CrashReportsClient`](../iMobileDevice.Managed/Services/CrashReportsClient.cs) 已支持触发搬移、列举、拉取与清理；IPS 解析待补 | P2 |
| [`MobileImageMounterService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobile_image_mounter.py#L42) | 开发者镜像挂载 | `raise_if_cannot_mount`、`copy_devices`、`list_images`、`lookup_image` | 待实现 | — | P4 |
| [`StorageMounterBridgeService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/storage_mounter_bridge.py#L22) | 设备存储挂载桥 | `invoke`、`copy_devices`、`lookup_image`、`query_developer_mode_status` | 待实现 | — | P2 |
| [`CryptexdService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/cryptexd.py#L380) | 安装和管理 Cryptex | `invoke`、`install`、`cryptex_nonce`、`auto_install_ddi` | 待实现 | — | P6 |
| [`fetch_cryptex_ddi`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/cryptexd.py#L213) | 获取开发者 Cryptex 镜像 | — | 待实现 | — | P6 |
| [`InstalledCryptex`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/cryptexd.py#L302) | 已装 Cryptex 模型 | — | 待实现 | — | P6 |
| [`BtPacketLoggerService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/bt_packet_logger.py#L125) | 蓝牙日志 | `watch`、`write_to_packetlogger`、`write_to_pcapng` | 待实现 | — | P6 |

## 系统与设备

[上游参考页](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/docs/api/system.md)

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`AmfiService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/amfi.py#L23) | 开发者模式和签名信任 | `reveal_developer_mode_option_in_ui`、`enable_developer_mode`、`enable_developer_mode_post_restart`、`trust_app_signer` | 待实现 | — | P4 |
| [`MobileActivationService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobile_activation.py#L48) | 设备激活 | `state`、`wait_for_activation_session`、`activate`、`deactivate` | 待实现 | — | P6 |
| [`NotificationProxyService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/notification_proxy.py#L49) | 本机 Darwin 通知 | `notify_post`、`notify_register_dispatch`、`notify_set_state`、`receive_notification` | 已有 | [`NotificationProxyClient`](../iMobileDevice.Managed/Services/NotificationProxyClient.cs) 支持订阅、发布、流式接收与全部标准通知常量 | P2 |
| [`NotificationEvent`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/notification_proxy.py#L15) | 通知事件模型 | `from_message` | 待实现 | — | P2 |
| [`RemoteNotificationProxyService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/notification_proxy.py#L142) | 远程通知 | `notify_post`、`notify_register_dispatch`、`notify_set_state`、`notify_get_state` | 待实现 | — | P2 |
| [`TimeSyncService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/timesync.py#L47) | 时钟交换与偏差测量 | `exchange`、`measure` | 待实现 | — | P6 |
| [`PowerAssertionService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/power_assertion.py#L8) | 电源断言 | `create_power_assertion` | 待实现 | — | P6 |
| [`HeartbeatService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/heartbeat.py#L10) | 设备心跳 | `start` | 待实现 | — | P1 |
| [`CompanionProxyService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/companion.py#L8) | 伴随设备代理 | `list`、`listen_for_devices`、`get_value`、`start_forwarding_service_port` | 待实现 | — | P6 |
| [`PreboardService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/preboard.py#L6) | 预启动 stashbag | `create_stashbag`、`commit` | 待实现 | — | P6 |
| [`RestoreService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/restore_service.py#L22) | 调用设备恢复服务 | `delay_recovery_image`、`enter_recovery`、`reboot`、`get_preflightinfo` | 待实现 | — | P6 |
| [`DtDeviceArbitration`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/device_arbitration.py#L8) | 设备占用仲裁 | `version`、`check_in`、`check_out` | 待实现 | — | P4 |
| [`IDAMService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/idam.py#L6) | 设备间音频与 MIDI | `configuration_inquiry`、`set_idam_configuration` | 待实现 | — | P6 |

## 基础协议（源码补充）

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`list_devices`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/usbmux.py#L496) | 枚举设备 | — | 已有 | [`ListDevicesAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs)，返回设备标识与连接类型 | P1 |
| [`select_device`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/usbmux.py#L525) | 按条件选择设备 | — | 已有 | [`SelectDeviceAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs) 按 UDID 和连接类型筛选，优先 USB，无匹配返回 null | P1 |
| [`get_preferred_pair_record`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/pair_records.py#L95) | 选择配对记录来源 | — | 部分 | [`ReadPairRecordAsync`](../iMobileDevice.Managed/Usbmuxd/UsbmuxdClient.cs)，可读 usbmuxd 配对库，缺多来源选择 | P1 |
| [`RemoteXPCConnection`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/remotexpc.py#L219) | RemoteXPC 消息和文件流 | `send_device_handshake`、`send_request`、`iter_file_chunks`、`receive_file` | 部分 | [`RemoteXpcConnection`](../iMobileDevice.Managed/RemoteXpc/RemoteXpcConnection.cs)，已支持 HTTP/2 请求应答，缺上游文件流 | P1 |
| [`CoreDeviceTunnelProxy`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/tunnel_service.py#L1388) | 通过 Lockdown 建立 CoreDevice 代理 | `create`、`remote_identifier`、`start_tcp_tunnel` | 部分 | [`CoreDeviceProxyClient`](../iMobileDevice.Managed/Tunnel/CoreDeviceProxyClient.cs)，17.4+ USB 握手和 IPv6 帧收发 | P1 |
| [`DTXConnection`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/dtx/connection.py#L42) | DTX 消息与事件分发 | `wait_disconnected`、`open_channel`、`open_channel`、`open_channel` | 部分 | [`InvokeAsync` 与 `Subscribe`](../iMobileDevice.Managed/Dvt/DtxConnection.cs) 已支持调用、多片重组与设备主动消息频道订阅 | P4 |
| [`DTXChannel`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/dtx/channel.py#L41) | 双向 DTX 通道 | `invoke`、`notify`、`send_data`、`send_keyed_message` | 部分 | [`InvokeAsync`](../iMobileDevice.Managed/Dvt/DtxConnection.cs)，可发送调用、重组回复，缺订阅设备主动消息 | P4 |
| [`DTXService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/dtx/service.py#L278) | 声明 DTX 服务及消息处理器 | `on_closed`、`shutdown_queue`、`dtxproxy`、`send_notification` | 待实现 | — | P4 |

## 文件、镜像和同步（源码补充）

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`MobileSyncService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobilesync.py#L28) | 同步记录读写与删除 | `get_all_records`、`push_changes` | 待实现 | — | P6 |
| [`DeviceLink`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/device_link.py#L115) | DeviceLink 消息循环和主机端文件操作 | `dl_loop`、`version_exchange`、`send_process_message`、`download_files` | 部分 | [DeviceLink](../iMobileDevice.Managed/Backup/DeviceLink.cs) 在备份 API 内部处理版本、文件传输和完成状态 | P3 |
| [`DeveloperDiskImageMounter`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobile_image_mounter.py#L377) | 传统开发者镜像 | `mount`、`umount` | 待实现 | — | P4 |
| [`PersonalizedImageMounter`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/mobile_image_mounter.py#L405) | 个性化镜像与签名 | `mount`、`umount`、`get_manifest_from_tss` | 待实现 | — | P4 |
| [`WebDavServer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/webdav.py#L309) | 通过 WebDAV 提供文件访问 | `url`、`stop` | 待实现 | — | P6 |

## 开发与自动化（源码补充）

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`Allocations`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/allocations.py#L245) | 内存分配跟踪 | `launch_environment`、`attach` | 待实现 | — | P4 |
| [`DyldMetrics`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/dyld_metrics.py#L25) | 动态加载指标 | `capture` | 待实现 | — | P4 |
| [`Leaks`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/leaks.py#L30) | 内存图 | `memory_graph` | 待实现 | — | P4 |
| [`VMTracking`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/instruments/vm_tracking.py#L160) | 虚拟内存区域 | `snapshot` | 待实现 | — | P4 |
| [`XCUITestService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/dvt/testmanaged/xcuitest.py#L94) | 执行 XCTest Runner | `run` | 待实现 | — | P5 |
| [`WebDriver`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/web_protocol/driver.py#L30) | 通过 Web Inspector 驱动页面 | `add_cookie`、`back`、`get_current_url`、`get_current_window_handle` | 待实现 | — | P5 |
| [`browser_debugger`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/services/web_protocol/cdp_server.py#L989) | 浏览器级 CDP WebSocket 会话 | — | 待实现 | — | P5 |

## CoreDevice 与媒体（源码补充）

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`AppServiceService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/app_service.py#L14) | 应用和进程管理 | `list_apps`、`launch_application`、`list_processes`、`stream_apps` | 待实现 | — | P4 |
| [`ConfigurationService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/configuration_service.py#L22) | 界面及显示配置 | `get_user_interface_style`、`set_user_interface_style`、`set_liquid_glass_opacity`、`get_color_filter` | 待实现 | — | P4 |
| [`DeviceInfoService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/device_info.py#L80) | 信息、锁定和显示状态 | `get_device_info`、`get_display_info`、`display_info_updates`、`query_mobilegestalt` | 待实现 | — | P4 |
| [`DiagnosticsServiceService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/diagnostics_service.py#L16) | 生成系统诊断归档 | `capture_sysdiagnose` | 待实现 | — | P4 |
| [`DisplayService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/display_service.py#L63) | 媒体流控制 | `get_media_support_info`、`get_media_stream_server_status`、`start_video_stream`、`start_audio_stream` | 待实现 | — | P5 |
| [`FileServiceService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/file_service.py#L64) | 文件域操作及通知 | `retrieve_directory_list`、`retrieve_file`、`propose_empty_file`、`file_system_operation` | 待实现 | — | P4 |
| [`IndigoHIDService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/hid_service.py#L408) | 硬件按键 | `send_button` | 待实现 | — | P5 |
| [`UniversalHIDServiceService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/hid_service.py#L435) | 虚拟 HID 输入 | `list_connected_services`、`touchscreen_service_id`、`active_touchscreen_service_id`、`send_report` | 待实现 | — | P5 |
| [`IconService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/icon_service.py#L34) | 应用图标 | `fetch_icon` | 待实现 | — | P4 |
| [`LocationService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/location_service.py#L7) | 位置模拟场景 | `available_location_scenarios` | 待实现 | — | P4 |
| [`OpenStdioSocketService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/open_stdio_socket.py#L9) | 进程标准输入输出 | `identifier`、`read`、`write` | 待实现 | — | P4 |
| [`OrientationService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/orientation_service.py#L34) | 屏幕方向 | `rotate` | 待实现 | — | P5 |
| [`PasteboardService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/pasteboard_service.py#L245) | 剪贴板 | `get`、`get_text`、`set`、`set_text` | 待实现 | — | P5 |
| [`ScreenCaptureService`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/screen_capture_service.py#L8) | 通过 CoreDevice 截屏 | `active_display_unique_id`、`capture_screenshot` | 待实现 | — | P5 |
| [`ScreenStreamServer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/screen_stream.py#L639) | 浏览器屏幕流 | `serve` | 待实现 | — | P5 |
| [`VncStreamServer`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/remote/core_device/vnc_server.py#L376) | VNC 屏幕流 | `serve` | 待实现 | — | P5 |

## 固件与恢复（源码补充）

| 上游 API 及源码 | 功能 | 代表成员 | Managed | C# 入口或边界 | 阶段 |
| --- | --- | --- | --- | --- | --- |
| [`Restore`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/restore/restore.py#L129) | 编排固件更新与恢复 | `handle_async_data_request_msg`、`send_filesystem`、`get_build_identity_from_request`、`send_buildidentity` | 待实现 | — | P6 |
| [`Recovery`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/restore/recovery.py#L24) | 恢复阶段及引导 | `reconnect_irecv`、`get_preboard_manifest`、`get_tss_response`、`get_local_policy_tss_response` | 待实现 | — | P6 |
| [`IRecv`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/irecv/client.py#L194) | 恢复/DFU USB 通信 | `device`、`mode`、`info`、`ecid` | 待实现 | — | P6 |
| [`TSSRequest`](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/restore/tss.py#L128) | 固件签名请求 | `apply_restore_request_rules`、`add_tags`、`setdefault_tags`、`tags` | 待实现 | — | P6 |

## 现有 Managed 协议基础

[Plist XML 与二进制编解码](../iMobileDevice.Managed/Plist/PlistSerializer.cs)、[配对证书及记录](../iMobileDevice.Managed/Lockdown/PairRecord.cs)、[XPC 序列化](../iMobileDevice.Managed/RemoteXpc/XpcSerializer.cs)、[NSKeyedArchiver](../iMobileDevice.Managed/Dvt/NsKeyedArchiver.cs) 已用于上述客户端。usbmuxd 另有热插拔事件及本机配对记录读写，AFC 有文件句柄和分块传输接口。
