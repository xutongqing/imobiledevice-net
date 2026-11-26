namespace iMobileDevice.Managed.RemoteXpc;

using System.Globalization;
using iMobileDevice.Managed.Tunnel;

public sealed record RsdServiceEntry(
    string Name,
    ushort Port,
    bool UsesRemoteXpc,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>
/// Pure managed Remote Service Discovery (RSD) client for iOS 17+ / 18 developer services over <see cref="UserspaceTcpTunnel"/>.
/// </summary>
public sealed class RemoteServiceDiscoveryClient : IAsyncDisposable
{
    private readonly UserspaceTcpTunnel tunnel;
    private readonly RemoteXpcConnection xpc;
    private readonly Dictionary<string, RsdServiceEntry> services;
    private readonly Dictionary<string, object?> peerProperties;

    private RemoteServiceDiscoveryClient(
        UserspaceTcpTunnel tunnel,
        RemoteXpcConnection xpc,
        Dictionary<string, RsdServiceEntry> services,
        Dictionary<string, object?> peerProperties)
    {
        this.tunnel = tunnel;
        this.xpc = xpc;
        this.services = services;
        this.peerProperties = peerProperties;
    }

    public IReadOnlyDictionary<string, RsdServiceEntry> Services => services;
    public IReadOnlyDictionary<string, object?> PeerProperties => peerProperties;

    public string? OsVersion => PeerProperties.TryGetValue("OSVersion", out object? v) ? v as string : null;
    public string? ProductType => PeerProperties.TryGetValue("ProductType", out object? v) ? v as string : null;
    public string? UniqueDeviceId => PeerProperties.TryGetValue("UniqueDeviceID", out object? v) ? v as string : null;

    public static async Task<RemoteServiceDiscoveryClient> ConnectAsync(UserspaceTcpTunnel tunnel, CancellationToken cancellationToken = default, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(tunnel);

        Stream rsdStream = await tunnel.ConnectAsync(tunnel.Parameters.ServerRsdPort, cancellationToken).ConfigureAwait(false);
        RemoteXpcConnection xpc = await RemoteXpcConnection.ConnectAsync(rsdStream, cancellationToken, trace).ConfigureAwait(false);
        try
        {
            // RSD pushes its Handshake dictionary on the server/reply channel (Stream 3) or root channel (Stream 1)
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<Dictionary<string, object?>> fromRoot = WaitForNonEmptyDictAsync(xpc.ReceiveRootMessageAsync, linked.Token);
            Task<Dictionary<string, object?>> fromReply = WaitForNonEmptyDictAsync(xpc.ReceiveReplyMessageAsync, linked.Token);
            Task<Dictionary<string, object?>> winner = await Task.WhenAny(fromRoot, fromReply).ConfigureAwait(false);
            linked.Cancel();
            Dictionary<string, object?> root = await winner.ConfigureAwait(false);

            var props = root.TryGetValue("Properties", out object? propsObj) && propsObj is Dictionary<string, object?> pDict
                ? pDict
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            var services = new Dictionary<string, RsdServiceEntry>(StringComparer.Ordinal);
            if (root.TryGetValue("Services", out object? svcObj) && svcObj is Dictionary<string, object?> sDict)
            {
                foreach (var kv in sDict)
                {
                    if (kv.Value is not Dictionary<string, object?> entry)
                    {
                        continue;
                    }

                    ushort port = ParsePort(entry.GetValueOrDefault("Port"));
                    var svcProps = entry.TryGetValue("Properties", out object? sp) && sp is Dictionary<string, object?> spDict
                        ? spDict
                        : new Dictionary<string, object?>(StringComparer.Ordinal);

                    bool usesRemoteXpc = svcProps.TryGetValue("UsesRemoteXPC", out object? urx) && urx is bool b && b;
                    services[kv.Key] = new RsdServiceEntry(kv.Key, port, usesRemoteXpc, svcProps);
                }
            }

            return new RemoteServiceDiscoveryClient(tunnel, xpc, services, props);
        }
        catch
        {
            await xpc.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public RsdServiceEntry GetService(string serviceName)
    {
        if (!services.TryGetValue(serviceName, out var entry))
        {
            throw new KeyNotFoundException($"RSD service '{serviceName}' not found. Available services: {services.Count}.");
        }

        return entry;
    }

    public Task<Stream> ConnectToServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        RsdServiceEntry entry = GetService(serviceName);
        return tunnel.ConnectAsync(entry.Port, cancellationToken);
    }

    private static async Task<Dictionary<string, object?>> WaitForNonEmptyDictAsync(
        Func<CancellationToken, ValueTask<XpcMessage>> reader,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            XpcMessage msg = await reader(cancellationToken).ConfigureAwait(false);
            if (msg.Body is Dictionary<string, object?> dict && dict.Count > 0)
            {
                return dict;
            }
        }
    }

    private static ushort ParsePort(object? val) => val switch
    {
        string s when ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort p) => p,
        long l => checked((ushort)l),
        ulong ul => checked((ushort)ul),
        _ => 0,
    };

    public ValueTask DisposeAsync() => xpc.DisposeAsync();
}
