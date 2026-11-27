namespace iMobileDevice.Managed.Services;

using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

public enum SpringBoardInterfaceOrientation
{
    Unknown = 0,
    Portrait = 1,
    PortraitUpsideDown = 2,
    LandscapeRight = 3,
    LandscapeLeft = 4,
}

/// <summary>
/// Pure managed client for <c>com.apple.springboardservices</c> (app icon PNG, wallpaper, orientation, icon layout).
/// </summary>
public sealed class SpringBoardServicesClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.springboardservices";

    private readonly Stream stream;
    private readonly PlistServiceWire wire;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private bool disposed;

    public SpringBoardServicesClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        wire = new PlistServiceWire(stream, PlistFormat.Xml);
    }

    public static async Task<SpringBoardServicesClient> ConnectAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new SpringBoardServicesClient(serviceStream);
    }

    public async Task<byte[]> GetIconPngDataAsync(string bundleIdentifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleIdentifier);
        PlistDictionary reply = await ExchangeAsync(new PlistDictionary
        {
            ["command"] = "getIconPNGData",
            ["bundleId"] = bundleIdentifier,
        }, cancellationToken).ConfigureAwait(false);

        byte[]? data = reply.GetData("pngData");
        if (data == null || data.Length == 0)
        {
            throw new InvalidDataException($"SpringBoard did not return icon PNG data for '{bundleIdentifier}'.");
        }

        return data;
    }

    public async Task<byte[]> GetHomeScreenWallpaperPngDataAsync(CancellationToken cancellationToken = default)
    {
        PlistDictionary reply = await ExchangeAsync(new PlistDictionary
        {
            ["command"] = "getHomeScreenWallpaperPNGData",
        }, cancellationToken).ConfigureAwait(false);

        byte[]? data = reply.GetData("pngData");
        if (data == null || data.Length == 0)
        {
            throw new InvalidDataException("SpringBoard did not return home screen wallpaper PNG data.");
        }

        return data;
    }

    public async Task<SpringBoardInterfaceOrientation> GetInterfaceOrientationAsync(CancellationToken cancellationToken = default)
    {
        PlistDictionary reply = await ExchangeAsync(new PlistDictionary
        {
            ["command"] = "getInterfaceOrientation",
        }, cancellationToken).ConfigureAwait(false);

        long? raw = reply.GetInt64("interfaceOrientation");
        return raw.HasValue && Enum.IsDefined(typeof(SpringBoardInterfaceOrientation), (int)raw.Value)
            ? (SpringBoardInterfaceOrientation)(int)raw.Value
            : SpringBoardInterfaceOrientation.Unknown;
    }

    public async Task<PlistArray> GetIconStateAsync(string? formatVersion = "2", CancellationToken cancellationToken = default)
    {
        var req = new PlistDictionary { ["command"] = "getIconState" };
        if (!string.IsNullOrEmpty(formatVersion))
        {
            req["formatVersion"] = formatVersion;
        }

        ObjectDisposedException.ThrowIf(disposed, this);
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await wire.SendAsync(req, cancellationToken).ConfigureAwait(false);
            PlistNode reply = await wire.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (reply is not PlistArray arr)
            {
                throw new InvalidDataException($"Expected PlistArray from SpringBoard getIconState, received {reply.Kind}.");
            }

            return arr;
        }
        finally
        {
            ioLock.Release();
        }
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
