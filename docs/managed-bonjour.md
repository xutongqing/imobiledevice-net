# 在局域网发现 iOS 服务

`BonjourClient` 通过 UDP 5353 浏览 mDNS/DNS-SD 服务。应用可在同一个超时窗口内获取 RemotePairing、手工配对和 Wi-Fi Lockdown（mobdev2）的广播。可读字段包括服务实例、目标主机、端口、TXT 名称和 IP 端点。

```csharp
using iMobileDevice.Managed.Bonjour;

var client = new BonjourClient();
var services = await client.BrowseAsync(
    [BonjourServiceTypes.RemotePairing, BonjourServiceTypes.RemotePairingManualPairing,
     BonjourServiceTypes.Mobdev2], TimeSpan.FromSeconds(5), cancellationToken);

foreach (var service in services)
    foreach (var endpoint in service.Endpoints)
        Console.WriteLine($"{service.Instance} {service.Name} {endpoint.ToIPEndPoint()}");
```

超时返回窗口内收集到的记录；调用方取消会抛出 `OperationCanceledException`。客户端在各自接收接口内关联 PTR、SRV、TXT 与地址，合并重复广告时保留 IPv6 链路本地地址的接口作用域。广播只说明设备公布了一个服务；要取得 mobdev2 的设备摘要，还需用已有配对凭据建立 Wi-Fi Lockdown 会话。

## Windows 同网对照（2026-11-27）

使用独立 Managed NuGet 与 pymobiledevice3 11.57.0 同时监听 5 秒。通过 Windows 的 `dns-sd` 临时注册普通 DNS-SD 测试服务，结束后撤销。未改动主机防火墙或 iPhone 配对设置。

| 场景 | 实际结果 |
| --- | --- |
| 一个明确的本地 IPv4 地址 | 两端各发现 1 个实例、1 个端点，实例、主机、端口、TXT 名称和地址均一致。 |
| 本机多地址广播 | 两端的实例、主机、端口和 TXT 名称相同。Managed 返回 7 个地址，Python 返回 4 个。Managed 保留广告中的地址；上游还会筛选能映射到本地网卡的地址。该次对照未逐个验证端点的连通性。 |
| 三种 Apple 服务 | 两端在当前网络均返回 0 条。真实 iPhone 的正向广播发现、链路本地 IPv6 连接、Wi-Fi Lockdown 凭据会话和 RSD 查询仍待实测或实现。 |

协议测试覆盖分包、乱序、多网卡同名设备、不同接口的 IPv6 作用域、撤回 PTR、超时及取消。两种 RemotePairing 已提供公开浏览入口，功能对照表保留“部分”以反映多地址筛选差异。[服务与传输测试](../iMobileDevice.Managed.Tests/BonjourDiscoveryTests.cs) · [DNS 报文测试](../iMobileDevice.Managed.Tests/BonjourWireCodecTests.cs) · [上游 Bonjour 实现](https://github.com/doronz88/pymobiledevice3/blob/9d54c30ed2a37c761ec43dbdb9eeaaa613daecb2/pymobiledevice3/bonjour.py)
