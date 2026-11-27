namespace iMobileDevice.Managed.Bonjour;

using System.Net;

/// <summary>
/// Represents a reachable IP endpoint discovered for a Bonjour service, preserving IPv6 link-local interface scope.
/// </summary>
public sealed record BonjourEndpoint(
    IPAddress Address,
    ushort Port,
    int? InterfaceIndex = null,
    string? InterfaceName = null)
{
    /// <summary>
    /// Formats the endpoint IP address, including the IPv6 link-local scope identifier (<c>fe80::...%scope</c>) when present.
    /// </summary>
    public string FormattedAddress
    {
        get
        {
            if (Address.IsIPv6LinkLocal)
            {
                if (Address.ScopeId > 0)
                {
                    return Address.ToString();
                }

                if (InterfaceIndex is > 0)
                {
                    return new IPAddress(Address.GetAddressBytes(), InterfaceIndex.Value).ToString();
                }
            }

            return Address.ToString();
        }
    }

    /// <summary>
    /// Converts this endpoint into an <see cref="IPEndPoint"/> preserving <see cref="IPAddress.ScopeId"/>.
    /// </summary>
    public IPEndPoint ToIPEndPoint() => new(Address, Port);
}

/// <summary>
/// Represents an aggregated Bonjour (DNS-SD) service discovery result.
/// </summary>
public sealed record BonjourServiceRecord(
    string ServiceType,
    string Instance,
    string HostName,
    ushort Port,
    IReadOnlyList<BonjourEndpoint> Endpoints,
    IReadOnlyDictionary<string, string?> TxtRecords,
    string? Name)
{
    /// <summary>
    /// Convenience view of the IP addresses associated with <see cref="Endpoints"/>.
    /// </summary>
    public IReadOnlyList<IPAddress> Addresses => Endpoints.Select(static endpoint => endpoint.Address).ToArray();
}

/// <summary>
/// Exception thrown when mDNS/DNS-SD discovery encounters an unrecoverable transport or configuration error.
/// </summary>
public sealed class BonjourException : Exception
{
    public BonjourException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
