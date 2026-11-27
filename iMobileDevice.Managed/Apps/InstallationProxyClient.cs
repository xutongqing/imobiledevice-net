namespace iMobileDevice.Managed.Apps;

using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Pure managed client for the Installation Proxy (<c>com.apple.mobile.installation_proxy</c>).
/// </summary>
/// <remarks>
/// Every request/response pair is serialized on one service connection. When the device reports an
/// error, the reply stream position is no longer trustworthy, so the client disposes itself before
/// the <see cref="AppsException"/> escapes; open a new connection to retry.
/// </remarks>
public sealed class InstallationProxyClient : IAsyncDisposable
{
    public const string ServiceName = "com.apple.mobile.installation_proxy";
    private static readonly string[] SizeAttributes = ["CFBundleIdentifier", "StaticDiskUsage", "DynamicDiskUsage"];

    private readonly Stream stream;
    private readonly AppsWire wire;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private int closed;

    public InstallationProxyClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        wire = new AppsWire(stream);
    }

    public static async Task<InstallationProxyClient> ConnectAsync(LockdownClient lockdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new InstallationProxyClient(serviceStream);
    }

    /// <summary>
    /// Lists installed applications, sorted by bundle identifier. Equivalent to <c>apps list</c>.
    /// </summary>
    /// <remarks>
    /// The list comes from <c>Browse</c>, which streams the result in batches until the device reports
    /// <c>Complete</c>. Disk usage is not part of a browse reply, so sizes are merged from a second
    /// <c>Lookup</c> pass. Whether placeholder applications appear is decided by the device.
    /// </remarks>
    /// <param name="type">Application type filter, sent as the <c>ApplicationType</c> option.</param>
    /// <param name="calculateSizes">Run the second lookup that adds <c>StaticDiskUsage</c> and <c>DynamicDiskUsage</c>.</param>
    /// <param name="showPlaceholders">Include placeholder applications that are not installed yet.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Applications in ordinal bundle identifier order; empty when nothing matches.</returns>
    public Task<IReadOnlyList<AppInfo>> ListAsync(
        AppListType type = AppListType.Any,
        bool calculateSizes = false,
        bool showPlaceholders = false,
        CancellationToken cancellationToken = default)
    {
        var options = new PlistDictionary { ["ApplicationType"] = OptionName(type) };
        if (showPlaceholders)
        {
            options["ShowPlaceholders"] = true;
        }

        return ExchangeAsync(async token =>
        {
            Dictionary<string, PlistDictionary> apps = await BrowseAsync(options, token).ConfigureAwait(false);
            if (calculateSizes)
            {
                await MergeSizesAsync(apps, options, token).ConfigureAwait(false);
            }

            return ToResult(apps);
        }, cancellationToken);
    }

    /// <summary>
    /// Looks up one or more installed applications by bundle identifier. Equivalent to <c>apps query</c>.
    /// </summary>
    /// <param name="bundleIdentifiers">Bundle identifiers sent as the <c>BundleIDs</c> option.</param>
    /// <param name="calculateSizes">Run the second lookup that adds <c>StaticDiskUsage</c> and <c>DynamicDiskUsage</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Applications that exist on the device, sorted by bundle identifier. An unknown bundle
    /// identifier is simply absent from the result; it is not an error.
    /// </returns>
    public Task<IReadOnlyList<AppInfo>> QueryAsync(
        IEnumerable<string> bundleIdentifiers,
        bool calculateSizes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleIdentifiers);
        var requested = new PlistArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string bundleIdentifier in bundleIdentifiers)
        {
            ArgumentException.ThrowIfNullOrEmpty(bundleIdentifier);
            if (seen.Add(bundleIdentifier))
            {
                requested.Add(bundleIdentifier);
            }
        }

        if (requested.Count == 0)
        {
            throw new ArgumentException("At least one bundle identifier is required.", nameof(bundleIdentifiers));
        }

        var options = new PlistDictionary
        {
            ["BundleIDs"] = requested,
            ["ApplicationType"] = OptionName(AppListType.Any),
        };

        return ExchangeAsync(async token =>
        {
            Dictionary<string, PlistDictionary> apps = await LookupAsync(options, token).ConfigureAwait(false);
            if (calculateSizes && apps.Count != 0)
            {
                await MergeSizesAsync(apps, options, token).ConfigureAwait(false);
            }

            return ToResult(apps);
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            ioLock.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<string, PlistDictionary>> BrowseAsync(PlistDictionary options, CancellationToken token)
    {
        await SendAsync(new PlistDictionary { ["Command"] = "Browse", ["ClientOptions"] = options }, token).ConfigureAwait(false);
        var apps = new Dictionary<string, PlistDictionary>(StringComparer.Ordinal);
        while (true)
        {
            PlistDictionary reply = await ReceiveReplyAsync("Browse", token).ConfigureAwait(false);
            if (reply.GetArray("CurrentList") is { } batch)
            {
                foreach (PlistNode entry in batch)
                {
                    // Batching can repeat an application; the last copy wins.
                    if (entry is PlistDictionary app && app.GetString("CFBundleIdentifier") is { } bundle)
                    {
                        apps[bundle] = app;
                    }
                }
            }

            if (reply.GetString("Status") == "Complete")
            {
                return apps;
            }

            if (reply.Count == 0)
            {
                throw new IOException("The device ended the Browse replies before reporting Complete.");
            }
        }
    }

    private async Task<Dictionary<string, PlistDictionary>> LookupAsync(PlistDictionary options, CancellationToken token)
    {
        await SendAsync(new PlistDictionary { ["Command"] = "Lookup", ["ClientOptions"] = options }, token).ConfigureAwait(false);
        var apps = new Dictionary<string, PlistDictionary>(StringComparer.Ordinal);
        PlistDictionary? last = null;
        bool sawResult = false;
        while (true)
        {
            PlistDictionary reply = await ReceiveReplyAsync("Lookup", token).ConfigureAwait(false);
            last = reply;
            if (reply.GetDictionary("LookupResult") is { } result)
            {
                sawResult = true;
                foreach ((string bundle, PlistNode info) in result)
                {
                    if (info is PlistDictionary app)
                    {
                        apps[bundle] = app;
                    }
                }
            }

            // A device that streams the result in batches keeps going until it reports Complete;
            // a single reply has either Complete or no Status at all.
            string? status = reply.GetString("Status");
            if (status is null || status == "Complete")
            {
                break;
            }
        }

        if (!sawResult)
        {
            if (last is { Count: 0 })
            {
                throw new IOException("The device ended the Lookup replies before sending a LookupResult.");
            }

            throw new AppsException("The Lookup reply is missing LookupResult.");
        }

        return apps;
    }

    private async Task MergeSizesAsync(Dictionary<string, PlistDictionary> apps, PlistDictionary options, CancellationToken token)
    {
        var sizeOptions = new PlistDictionary();
        foreach ((string key, PlistNode value) in options)
        {
            sizeOptions[key] = value;
        }

        sizeOptions["ReturnAttributes"] = new PlistArray(SizeAttributes.Select(static attribute => (PlistNode)attribute));
        Dictionary<string, PlistDictionary> sizes = await LookupAsync(sizeOptions, token).ConfigureAwait(false);
        foreach ((string bundle, PlistDictionary usage) in sizes)
        {
            // The second lookup can only narrow the first one; an unknown bundle means the
            // application disappeared between the two requests, so it adds nothing.
            if (apps.TryGetValue(bundle, out PlistDictionary? app))
            {
                foreach ((string key, PlistNode value) in usage)
                {
                    app[key] = value;
                }
            }
        }
    }

    private async Task SendAsync(PlistNode value, CancellationToken token) =>
        await wire.SendAsync(value, token).ConfigureAwait(false);

    private async Task<PlistDictionary> ReceiveReplyAsync(string command, CancellationToken token)
    {
        PlistNode node = await wire.ReceiveAsync(token).ConfigureAwait(false);
        if (node is not PlistDictionary reply)
        {
            throw new AppsException($"The device sent a {node.Kind} reply to {command} instead of a dictionary.");
        }

        if (reply.GetString("Error") is { } error)
        {
            throw new AppsException($"Installation Proxy {command} failed: {error}.", error, reply.GetString("ErrorDescription"));
        }

        return reply;
    }

    private async Task<T> ExchangeAsync<T>(Func<CancellationToken, Task<T>> exchange, CancellationToken token)
    {
        await ioLock.WaitAsync(token).ConfigureAwait(false);
        bool started = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != 0, this);
            token.ThrowIfCancellationRequested();
            started = true;
            return await exchange(token).ConfigureAwait(false);
        }
        catch when (started)
        {
            // A partial frame or a device error makes the next exchange unsafe on this connection.
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (Volatile.Read(ref closed) == 0)
            {
                ioLock.Release();
            }
        }
    }

    private static IReadOnlyList<AppInfo> ToResult(Dictionary<string, PlistDictionary> apps) =>
        apps.OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => new AppInfo(entry.Key, entry.Value))
            .ToList();

    private static string OptionName(AppListType type) => type switch
    {
        AppListType.Any => "Any",
        AppListType.User => "User",
        AppListType.System => "System",
        AppListType.Hidden => "Hidden",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown application type."),
    };
}
