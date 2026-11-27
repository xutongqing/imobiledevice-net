namespace iMobileDevice.Managed.Bonjour;

/// <summary>
/// Cancellable, timeout-bounded mDNS/DNS-SD discovery client for iOS Bonjour services.
/// </summary>
public sealed class BonjourClient
{
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly Func<IMdnsTransport> transportFactory;

    /// <summary>
    /// Initializes a new <see cref="BonjourClient"/> using the default UDP multicast transport.
    /// </summary>
    public BonjourClient()
        : this(static () => new UdpMdnsTransport())
    {
    }

    internal BonjourClient(Func<IMdnsTransport> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        this.transportFactory = transportFactory;
    }

    /// <summary>
    /// Default browse window matching <c>pymobiledevice3</c> (<c>2s</c> on Windows, <c>3s</c> on POSIX).
    /// </summary>
    public static TimeSpan DefaultTimeout => OperatingSystem.IsWindows()
        ? TimeSpan.FromSeconds(2)
        : TimeSpan.FromSeconds(3);

    /// <summary>
    /// Browses the local network for a single DNS-SD service type within a bounded timeout window.
    /// </summary>
    public Task<IReadOnlyList<BonjourServiceRecord>> BrowseAsync(
        string serviceType,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);
        return BrowseAsync([serviceType], timeout, cancellationToken);
    }

    /// <summary>
    /// Browses the local network for one or more DNS-SD service types within a bounded timeout window.
    /// </summary>
    /// <remarks>
    /// When the browse timeout expires, all records accumulated during the window are returned (empty list if none).
    /// When <paramref name="cancellationToken"/> is cancelled by the caller, the transport is disposed promptly and
    /// <see cref="OperationCanceledException"/> is propagated.
    /// </remarks>
    public async Task<IReadOnlyList<BonjourServiceRecord>> BrowseAsync(
        IReadOnlyCollection<string> serviceTypes,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceTypes);

        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effectiveTimeout, TimeSpan.Zero, nameof(timeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(effectiveTimeout, MaxTimeout, nameof(timeout));

        var normalizedTypes = new List<string>(serviceTypes.Count);
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string serviceType in serviceTypes)
        {
            string normalized = BonjourServiceTypes.Normalize(serviceType);
            if (seenTypes.Add(normalized))
            {
                normalizedTypes.Add(normalized);
            }
        }

        if (normalizedTypes.Count == 0)
        {
            throw new ArgumentException("At least one service type must be specified.", nameof(serviceTypes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // mDNS names are link-local. Join PTR/SRV/TXT/address records only on the receiving interface.
        var links = new Dictionary<int, BonjourDiscoveryAccumulator>();
        var ptrQuestions = normalizedTypes
            .Select(static serviceType => new DnsQuestion(DnsWireName.Parse(serviceType), DnsRecordType.PTR))
            .ToArray();
        byte[] initialQuery = DnsWireCodec.EncodeQuestions(ptrQuestions);

        await using IMdnsTransport transport = transportFactory();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(effectiveTimeout);

        try
        {
            await transport.SendQueryAsync(initialQuery, deadline.Token).ConfigureAwait(false);

            while (true)
            {
                MdnsReceivedPacket? packet = await transport.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (packet is null)
                {
                    break;
                }

                if (!links.TryGetValue(packet.Value.InterfaceIndex, out var accumulator))
                {
                    links[packet.Value.InterfaceIndex] = accumulator = new BonjourDiscoveryAccumulator(normalizedTypes);
                }
                if (!accumulator.AddPacket(packet.Value))
                {
                    continue;
                }

                IReadOnlyList<DnsQuestion> followUps = accumulator.TakePendingFollowUpQuestions();
                if (followUps.Count > 0)
                {
                    var encodableFollowUps = new List<DnsQuestion>(followUps.Count);
                    foreach (DnsQuestion followUp in followUps)
                    {
                        if (DnsWireCodec.CanEncodeName(followUp.Name))
                        {
                            encodableFollowUps.Add(followUp);
                        }
                    }

                    if (encodableFollowUps.Count > 0)
                    {
                        try
                        {
                            byte[] followUpPacket = DnsWireCodec.EncodeQuestions(encodableFollowUps);
                            await transport.SendQueryAsync(followUpPacket, deadline.Token).ConfigureAwait(false);
                        }
                        catch (ArgumentException)
                        {
                            // Ignore unencodable follow-up names derived from malformed remote packets.
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Browse window elapsed cleanly; return discoveries collected so far.
        }

        cancellationToken.ThrowIfCancellationRequested();
        return BonjourDiscoveryAccumulator.BuildResults(links.Values);
    }

    /// <summary>
    /// Discovers iOS 17+ Wi-Fi RemotePairing services (<see cref="BonjourServiceTypes.RemotePairing"/>).
    /// </summary>
    public Task<IReadOnlyList<BonjourServiceRecord>> BrowseRemotePairingAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => BrowseAsync(BonjourServiceTypes.RemotePairing, timeout, cancellationToken);

    /// <summary>
    /// Discovers manual-pairing RemotePairing services (<see cref="BonjourServiceTypes.RemotePairingManualPairing"/>),
    /// exposing advertised TXT <c>name</c> values when present.
    /// </summary>
    public Task<IReadOnlyList<BonjourServiceRecord>> BrowseRemotePairingManualPairingAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => BrowseAsync(BonjourServiceTypes.RemotePairingManualPairing, timeout, cancellationToken);

    /// <summary>
    /// Discovers advertised Wi-Fi Lockdown (<see cref="BonjourServiceTypes.Mobdev2"/>) mDNS endpoints.
    /// </summary>
    /// <remarks>
    /// Partial coverage compared to <c>pymobiledevice3 bonjour mobdev2</c>: returns advertised mDNS endpoints, ports,
    /// and TXT properties only. Connecting to Wi-Fi Lockdown with pairing credentials to read device summaries and
    /// performing RSD queries belong to a follow-up PR.
    /// </remarks>
    public Task<IReadOnlyList<BonjourServiceRecord>> BrowseMobdev2Async(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => BrowseAsync(BonjourServiceTypes.Mobdev2, timeout, cancellationToken);
}
