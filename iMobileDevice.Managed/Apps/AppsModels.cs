namespace iMobileDevice.Managed.Apps;

using iMobileDevice.Managed.Plist;

/// <summary>
/// Value of the Installation Proxy <c>ApplicationType</c> option, matching <c>apps list --type</c>.
/// </summary>
public enum AppListType
{
    Any,
    User,
    System,
    Hidden,
}

/// <summary>
/// Which directory tree House Arrest vends for an application.
/// </summary>
public enum AppContainerScope
{
    /// <summary>Only the application's <c>Documents</c> directory (<c>VendDocuments</c>).</summary>
    Documents,

    /// <summary>The whole application container (<c>VendContainer</c>).</summary>
    Container,
}

/// <summary>
/// One application as reported by the Installation Proxy.
/// </summary>
/// <param name="BundleIdentifier">Value of <c>CFBundleIdentifier</c>.</param>
/// <param name="Properties">Raw per-application metadata returned by the device.</param>
public sealed record AppInfo(string BundleIdentifier, PlistDictionary Properties)
{
    public string? ApplicationType => Properties.GetString("ApplicationType");
    public string? DisplayName => Properties.GetString("CFBundleDisplayName");
    public string? Version => Properties.GetString("CFBundleShortVersionString");

    /// <summary>Static disk usage in bytes, present only when sizes were requested.</summary>
    public long? StaticDiskUsage => Properties.GetInt64("StaticDiskUsage");

    /// <summary>Dynamic disk usage in bytes, present only when sizes were requested.</summary>
    public long? DynamicDiskUsage => Properties.GetInt64("DynamicDiskUsage");

    public bool IsPlaceholder => Properties.GetBoolean("IsPlaceholder") ?? false;
}

/// <summary>
/// The device answered an application request with <c>Error</c>. The service stream is left unusable,
/// so the client that produced this exception is disposed.
/// </summary>
public class AppsException : Exception
{
    public AppsException(string message, string? deviceError = null, string? deviceErrorDescription = null)
        : base(message)
    {
        DeviceError = deviceError;
        DeviceErrorDescription = deviceErrorDescription;
    }

    public string? DeviceError { get; }
    public string? DeviceErrorDescription { get; }
}

/// <summary>
/// The device refused to open an application container because no application with that bundle
/// identifier is installed (<c>ApplicationLookupFailed</c>).
/// </summary>
public sealed class AppNotInstalledException : AppsException
{
    public AppNotInstalledException(string message, string? deviceError = null)
        : base(message, deviceError)
    {
    }
}
