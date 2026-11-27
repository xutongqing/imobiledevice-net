namespace iMobileDevice.Managed.Bonjour;

/// <summary>
/// Well-known Bonjour (DNS-SD) service types used by Apple mobile devices,
/// aligned with <c>pymobiledevice3</c> (<c>9d54c30</c>).
/// </summary>
public static class BonjourServiceTypes
{
    /// <summary>Service type for iOS 17+ Wi-Fi RemotePairing tunnels.</summary>
    public const string RemotePairing = "_remotepairing._tcp.local.";

    /// <summary>Service type for manual-pairing capable RemotePairing devices.</summary>
    public const string RemotePairingManualPairing = "_remotepairing-manual-pairing._tcp.local.";

    /// <summary>Service type advertised by a host for device-initiated RemotePairing.</summary>
    public const string RemotePairingPairableHost = "_remotepairing-pairable-host._tcp.local.";

    /// <summary>Service type for Wi-Fi Lockdown (<c>lockdownd</c>) discovery.</summary>
    public const string Mobdev2 = "_apple-mobdev2._tcp.local.";

    /// <summary>Service type for legacy wireless PIN pairing.</summary>
    public const string Pairable = "_apple-pairable._tcp.local.";

    /// <summary>Service type for Remote Service Discovery (RSD).</summary>
    public const string Remoted = "_remoted._tcp.local.";

    /// <summary>
    /// Trims whitespace and ensures a trailing dot on a DNS-SD service type or FQDN.
    /// </summary>
    public static string Normalize(string serviceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);

        string trimmed = serviceType.Trim();
        if (trimmed.TrimEnd('.').Length == 0)
        {
            throw new ArgumentException("Service type cannot consist solely of dots.", nameof(serviceType));
        }

        return trimmed.EndsWith('.') ? trimmed : string.Concat(trimmed, ".");
    }

    internal static string StripTrailingDot(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().TrimEnd('.');
    }
}
