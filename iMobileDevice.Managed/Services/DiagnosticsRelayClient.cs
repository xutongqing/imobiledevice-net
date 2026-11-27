namespace iMobileDevice.Managed.Services;

using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

public sealed record BatteryInfo(
    long? CurrentCapacity,
    bool? IsCharging,
    bool? ExternalConnected,
    bool? FullyCharged,
    long? CycleCount,
    long? DesignCapacity,
    long? NominalChargeCapacity,
    long? TemperatureDeciCelsius,
    long? VoltageMillivolts,
    long? InstantAmperageMilliamps,
    PlistDictionary RawProperties)
{
    public double? TemperatureCelsius => TemperatureDeciCelsius.HasValue ? TemperatureDeciCelsius.Value / 100.0 : null;

    public static BatteryInfo FromIoRegistry(PlistDictionary dict)
    {
        ArgumentNullException.ThrowIfNull(dict);
        return new BatteryInfo(
            CurrentCapacity: dict.GetInt64("CurrentCapacity"),
            IsCharging: dict.GetBoolean("IsCharging"),
            ExternalConnected: dict.GetBoolean("ExternalConnected"),
            FullyCharged: dict.GetBoolean("FullyCharged"),
            CycleCount: dict.GetInt64("CycleCount"),
            DesignCapacity: dict.GetInt64("DesignCapacity"),
            NominalChargeCapacity: dict.GetInt64("NominalChargeCapacity") ?? dict.GetInt64("AppleRawMaxCapacity"),
            TemperatureDeciCelsius: dict.GetInt64("Temperature"),
            VoltageMillivolts: dict.GetInt64("Voltage"),
            InstantAmperageMilliamps: dict.GetInt64("InstantAmperage"),
            RawProperties: dict);
    }
}

public sealed class DiagnosticsException : Exception
{
    public DiagnosticsException(string message, string? status = null)
        : base(message)
    {
        Status = status;
    }

    public string? Status { get; }
}

/// <summary>
/// Pure managed client for <c>com.apple.mobile.diagnostics_relay</c> (IORegistry, Battery, MobileGestalt, power actions).
/// </summary>
public sealed class DiagnosticsRelayClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.mobile.diagnostics_relay";

    private readonly Stream stream;
    private readonly PlistServiceWire wire;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private bool disposed;

    public DiagnosticsRelayClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        wire = new PlistServiceWire(stream, PlistFormat.Xml);
    }

    public static async Task<DiagnosticsRelayClient> ConnectAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new DiagnosticsRelayClient(serviceStream);
    }

    public async Task<PlistDictionary> QueryDiagnosticsAsync(string type = "All", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        PlistDictionary reply = await ExchangeAsync(new PlistDictionary { ["Request"] = type }, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, type);
        return reply.GetDictionary("Diagnostics") ?? new PlistDictionary();
    }

    public async Task<PlistDictionary> QueryIoRegistryAsync(string? entryName = null, string? entryClass = null, CancellationToken cancellationToken = default)
    {
        var req = new PlistDictionary { ["Request"] = "IORegistry" };
        if (!string.IsNullOrEmpty(entryName))
        {
            req["EntryName"] = entryName;
        }

        if (!string.IsNullOrEmpty(entryClass))
        {
            req["EntryClass"] = entryClass;
        }

        PlistDictionary reply = await ExchangeAsync(req, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, "IORegistry");
        PlistDictionary diagnostics = reply.GetDictionary("Diagnostics") ?? new PlistDictionary();
        return diagnostics.GetDictionary("IORegistry") ?? diagnostics;
    }

    public async Task<BatteryInfo> GetBatteryInfoAsync(CancellationToken cancellationToken = default)
    {
        PlistDictionary ioReg = await QueryIoRegistryAsync(entryClass: "IOPMPowerSource", cancellationToken: cancellationToken).ConfigureAwait(false);
        return BatteryInfo.FromIoRegistry(ioReg);
    }

    public async Task<PlistDictionary> QueryMobileGestaltAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyArray = new PlistArray();
        foreach (string key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                keyArray.Add(key);
            }
        }

        if (keyArray.Count == 0)
        {
            throw new ArgumentException("At least one MobileGestalt key is required.", nameof(keys));
        }

        var req = new PlistDictionary
        {
            ["Request"] = "MobileGestalt",
            ["MobileGestaltKeys"] = keyArray,
        };

        PlistDictionary reply = await ExchangeAsync(req, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, "MobileGestalt");
        PlistDictionary diagnostics = reply.GetDictionary("Diagnostics") ?? new PlistDictionary();
        return diagnostics.GetDictionary("MobileGestalt") ?? diagnostics;
    }

    public async Task SleepAsync(CancellationToken cancellationToken = default)
    {
        PlistDictionary reply = await ExchangeAsync(new PlistDictionary { ["Request"] = "Sleep" }, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, "Sleep");
    }

    public async Task RestartAsync(bool waitForDisconnect = false, bool displayPass = false, CancellationToken cancellationToken = default)
    {
        var req = new PlistDictionary { ["Request"] = "Restart" };
        if (waitForDisconnect)
        {
            req["WaitForDisconnect"] = true;
        }

        if (displayPass)
        {
            req["DisplayPass"] = true;
        }

        PlistDictionary reply = await ExchangeAsync(req, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, "Restart");
    }

    public async Task ShutdownAsync(bool waitForDisconnect = false, bool displayPass = false, CancellationToken cancellationToken = default)
    {
        var req = new PlistDictionary { ["Request"] = "Shutdown" };
        if (waitForDisconnect)
        {
            req["WaitForDisconnect"] = true;
        }

        if (displayPass)
        {
            req["DisplayPass"] = true;
        }

        PlistDictionary reply = await ExchangeAsync(req, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, "Shutdown");
    }

    public async Task GoodbyeAsync(CancellationToken cancellationToken = default)
    {
        PlistDictionary reply = await ExchangeAsync(new PlistDictionary { ["Request"] = "Goodbye" }, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(reply, "Goodbye");
    }

    private async Task<PlistDictionary> ExchangeAsync(PlistDictionary request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await wire.ExchangeDictionaryAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private static void EnsureSuccess(PlistDictionary reply, string request)
    {
        string? status = reply.GetString("Status");
        if (!string.Equals(status, "Success", StringComparison.Ordinal))
        {
            throw new DiagnosticsException($"DiagnosticsRelay '{request}' failed with status '{status ?? "missing"}'.", status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await stream.DisposeAsync().ConfigureAwait(false);
        ioLock.Dispose();
    }
}
