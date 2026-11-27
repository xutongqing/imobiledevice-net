# P0 Windows 真机验收 · 2026-11-27

Windows 11 企业版 x64（build 26300），.NET SDK 10.0.403，运行时 .NET 8.0.33。两台 iPhone 先后通过 USB 接入，包通过本机 Apple 设备服务的默认端口 27015 通信。

## 实测结果

| 操作 | iPhone14,5 · iOS 15.1（19B74） | iPhone17,1 · iOS 27.3（24C5049d） |
| --- | --- | --- |
| USB 枚举 | 通过，返回 1 台 USB 设备 | 通过，换机后返回 1 台 USB 设备，机型与系统版本均已更新 |
| 复用已有配对 | 通过，建立 Lockdown TLS 会话，读取设备信息，停止并重建会话 | 通过，同样完成 TLS 会话重建。恢复原主机信任后又用独立进程复验 |
| 新身份配对 | 本机仅验证已有配对 | 通过，`PairAsync(saveToUsbmuxd: false)` 生成临时主机身份，以新凭证建立 TLS 并读取设备信息 |
| AFC | 通过，空文件和 1,573,641 字节随机文件读写、定位读取、改名和清理 | 通过，相同测试在原主机身份恢复后再次通过 |
| DVT 截屏 | 17.4+ 入口不适用。本次尝试旧版 DVT 服务时收到 `InvalidService`，镜像查询返回的 Developer 签名数为 0；未获得截图 | 通过，`CoreDeviceProxy → UserspaceTcpTunnel → RSD → DvtClient.TakeScreenshotAsync`。RSD 返回 85 项服务。最终复验的 PNG 为 **1206 × 2622，7,910,031 字节**，Windows 图像解码器成功读取像素 |

AFC 每次在新建的随机目录中测试。大文件写入后核对大小，从偏移 1,100,000 读取 64 字节并与原数据比较，改名后读回完整文件并核对 SHA-256。清理后重新列举根目录，确认测试目录已删除。

### 配对测试对原主机信任的影响

iOS 27.3 设备原先已信任这台 Windows 主机。测试通过 `PairAsync` 创建了不同的 HostID，并用新凭证启动了加密会话。清理临时身份后，设备拒绝了原 HostID 的会话请求，返回 `InvalidHostID`。

随后用原主机身份和证书重新配对，将设备新签发的 `EscrowBag` 更新到 Windows 原配对记录，回读核对整条记录。最后启动独立进程，从本机配对库重新取证书，验证 TLS 会话重建、携带 EscrowBag 启动 `com.apple.mobilebackup2`、AFC 文件往返和 DVT 截屏全部成功。备份服务验证止于服务连接，本次未执行备份。

## 包来源与复核方式

| 项目 | 记录 |
| --- | --- |
| 源码提交 | [`d1a490e`](https://github.com/xutongqing/imobiledevice-net/commit/d1a490e8e48baa1900d665415ba566c10592fc9f) |
| GitHub Actions | [第 12 次运行、第 1 次尝试](https://github.com/xutongqing/imobiledevice-net/actions/runs/45932587564)。[独立 `pack-managed` 作业](https://github.com/xutongqing/imobiledevice-net/actions/runs/45932587564/job/140288373817)的打包、隔离消费验证和上传步骤均成功 |
| 制品 | [imobiledevice-managed-nupkg · 15168729930](https://github.com/xutongqing/imobiledevice-net/actions/runs/45932587564/artifacts/15168729930) |
| 制品 ZIP SHA-256 | `70c16584d1cca8f5a782df9bac4976abc90f49901497c5522bf5481b4b52a23f`，与 Actions 记录一致 |
| 内层 NuGet 包 | `iMobileDevice.Managed.0.1.0-ci.12.1.nupkg`，97,635 字节 |
| NuGet SHA-256 | `6d3b5bfb18a3ff1d987e8abe136a67c4c3828192a4e8e8ff636698590257fc2d` |

将制品解压到本地包源，使用全新的 `net8.0` 控制台项目，仅引用精确版本 `[0.1.0-ci.12.1]`，使用空 NuGet 缓存还原。运行时核对程序集信息版本为 `0.1.0-ci.12.1+d1a490e8e48baa1900d665415ba566c10592fc9f`。公开 API 的设备连接及截屏调用示例见[仓库 README](../README.md)，对应协议断言见 [USB / Lockdown / AFC 用例](../iMobileDevice.Managed.Tests/ManagedDeviceE2ETests.cs) 和 [RSD / DVT 用例](../iMobileDevice.Managed.Tests/RsdAndDvtE2ETests.cs)。
