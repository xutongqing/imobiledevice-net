namespace iMobileDevice.Managed.Apps;

using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Opens an installed application's container through House Arrest
/// (<c>com.apple.mobile.house_arrest</c>) and hands the vended session to <see cref="AfcClient"/>.
/// </summary>
public static class AppContainer
{
    public const string ServiceName = "com.apple.mobile.house_arrest";
    private const string VendDocuments = "VendDocuments";
    private const string VendContainer = "VendContainer";
    private const string ApplicationLookupFailed = "ApplicationLookupFailed";

    /// <summary>
    /// Starts a House Arrest service on an existing Lockdown session, vends the requested directory
    /// and returns an AFC client using the paths exposed by the device.
    /// </summary>
    /// <param name="lockdown">Paired Lockdown session used to start the service.</param>
    /// <param name="bundleIdentifier">Bundle identifier of the application to open.</param>
    /// <param name="scope">Vend only <c>Documents</c> or the whole container.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>An AFC session that owns the service stream. For Documents scope, use <c>/Documents</c>.</returns>
    public static async Task<AfcClient> OpenAsync(
        LockdownClient lockdown,
        string bundleIdentifier,
        AppContainerScope scope = AppContainerScope.Documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockdown);
        ArgumentException.ThrowIfNullOrEmpty(bundleIdentifier);
        Stream serviceStream = await lockdown.ConnectToServiceAsync(ServiceName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await OpenAsync(serviceStream, bundleIdentifier, scope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Vends the requested directory on an already-connected House Arrest service stream and returns
    /// an AFC client that owns the stream from then on.
    /// </summary>
    /// <param name="serviceStream">Connected <c>com.apple.mobile.house_arrest</c> service stream.</param>
    /// <param name="bundleIdentifier">Bundle identifier of the application to open.</param>
    /// <param name="scope">Vend only <c>Documents</c> or the whole container.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>An AFC session that owns the service stream. For Documents scope, use <c>/Documents</c>.</returns>
    /// <remarks>
    /// The stream is disposed whenever the call fails, including a refused vend, a cancelled request and
    /// an invalid bundle identifier, so a failed call never leaks the service connection. A successful
    /// call transfers ownership to the returned client.
    /// </remarks>
    public static async Task<AfcClient> OpenAsync(
        Stream serviceStream,
        string bundleIdentifier,
        AppContainerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceStream);

        try
        {
            ArgumentException.ThrowIfNullOrEmpty(bundleIdentifier);
            cancellationToken.ThrowIfCancellationRequested();
            var wire = new AppsWire(serviceStream);
            await wire.SendAsync(new PlistDictionary
            {
                ["Command"] = scope switch
                {
                    AppContainerScope.Documents => VendDocuments,
                    AppContainerScope.Container => VendContainer,
                    _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown container scope."),
                },
                ["Identifier"] = bundleIdentifier,
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            PlistNode node = await wire.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (node is not PlistDictionary reply)
            {
                throw new AppsException($"The device sent a {node.Kind} reply to the vend request instead of a dictionary.");
            }

            if (reply.GetString("Error") is { } error)
            {
                throw error == ApplicationLookupFailed
                    ? new AppNotInstalledException($"No installed application has the bundle identifier '{bundleIdentifier}'.", error)
                    : new AppsException(
                        $"The device refused to open the container of '{bundleIdentifier}': {error}.",
                        error,
                        reply.GetString("ErrorDescription"));
            }

            return new AfcClient(serviceStream);
        }
        catch
        {
            await serviceStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
