namespace iMobileDevice.Managed.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using iMobileDevice.Managed.Bonjour;
using Xunit;

public class BonjourDiscoveryTests
{
    [Fact]
    public void ServiceTypeConstantsMatchPymobiledevice3Baseline()
    {
        Assert.Equal("_remotepairing._tcp.local.", BonjourServiceTypes.RemotePairing);
        Assert.Equal("_remotepairing-manual-pairing._tcp.local.", BonjourServiceTypes.RemotePairingManualPairing);
        Assert.Equal("_remotepairing-pairable-host._tcp.local.", BonjourServiceTypes.RemotePairingPairableHost);
        Assert.Equal("_apple-mobdev2._tcp.local.", BonjourServiceTypes.Mobdev2);
        Assert.Equal("_apple-pairable._tcp.local.", BonjourServiceTypes.Pairable);
        Assert.Equal("_remoted._tcp.local.", BonjourServiceTypes.Remoted);

        Assert.Equal("_remotepairing._tcp.local.", BonjourServiceTypes.Normalize("  _remotepairing._tcp.local  "));
        Assert.Equal("_remotepairing._tcp.local.", BonjourServiceTypes.Normalize("_remotepairing._tcp.local."));
        Assert.Throws<ArgumentException>(() => BonjourServiceTypes.Normalize("..."));

        TimeSpan expectedDefault = OperatingSystem.IsWindows() ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(3);
        Assert.Equal(expectedDefault, BonjourClient.DefaultTimeout);
    }

    [Fact]
    public async Task CorrelatesSplitAndOutOfOrderPacketsAcrossSections()
    {
        const string instance = "9466B801-AABB._remotepairing._tcp.local.";
        const string host = "iphone-15.local.";

        var transport = new FakeMdnsTransport();
        // Packet 1: AAAA in AR and TXT in NS arrive BEFORE PTR and SRV!
        transport.EnqueueResponse(
            BuildResponsePacket(
                authorities: [TxtRecord(instance, ("identifier", "id-1"), ("authTag", "tag-1"))],
                additional: [AaaaRecord(host, IPAddress.Parse("2001:db8::10"))]),
            interfaceIndex: 12,
            interfaceName: "Wi-Fi");

        // Packet 2: PTR in AN
        transport.EnqueueResponse(
            BuildResponsePacket(answers: [PtrRecord(BonjourServiceTypes.RemotePairing, instance)]),
            interfaceIndex: 12,
            interfaceName: "Wi-Fi");

        // Packet 3: SRV in AN and A in AR
        transport.EnqueueResponse(
            BuildResponsePacket(
                answers: [SrvRecord(instance, host, 49152)],
                additional: [ARecord(host, IPAddress.Parse("192.168.50.10"))]),
            interfaceIndex: 12,
            interfaceName: "Wi-Fi");

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(80));

        BonjourServiceRecord service = Assert.Single(results);
        Assert.Equal(BonjourServiceTypes.RemotePairing, service.ServiceType);
        Assert.Equal(instance, service.Instance);
        Assert.Equal("iphone-15.local", service.HostName);
        Assert.Equal((ushort)49152, service.Port);
        Assert.Null(service.Name);
        Assert.Equal("id-1", service.TxtRecords["identifier"]);
        Assert.Equal("tag-1", service.TxtRecords["authTag"]);
        Assert.Equal(2, service.Endpoints.Count);
        Assert.Contains(service.Endpoints, ep => ep.Address.Equals(IPAddress.Parse("2001:db8::10")) && ep.Port == 49152);
        Assert.Contains(service.Endpoints, ep => ep.Address.Equals(IPAddress.Parse("192.168.50.10")) && ep.Port == 49152);
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public async Task EmitsTargetedFollowUpQueriesForMissingSrvTxtAndAddresses()
    {
        const string instance = "dev-followup._remotepairing._tcp.local.";
        const string host = "followup-host.local.";

        var transport = new FakeMdnsTransport();
        transport.OnQuerySent = qPacket =>
        {
            Assert.True(DnsWireCodec.TryDecodeMessage(qPacket, out DnsMessage query));
            if (query.Questions.Any(q => q.Type == DnsRecordType.PTR))
            {
                // Initial response: PTR only (no inline SRV/TXT/A/AAAA)
                transport.EnqueueResponse(BuildResponsePacket(
                    answers: [PtrRecord(BonjourServiceTypes.RemotePairing, instance)]));
            }
            else if (query.Questions.Any(q => q.Type == DnsRecordType.SRV && q.Name.DisplayName == instance))
            {
                // Follow-up response #1: SRV + TXT, still missing A/AAAA
                transport.EnqueueResponse(BuildResponsePacket(
                    answers:
                    [
                        SrvRecord(instance, host, 55000),
                        TxtRecord(instance, ("identifier", "abc")),
                    ]));
            }
            else if (query.Questions.Any(q => q.Type == DnsRecordType.A && q.Name.DisplayName == host))
            {
                // Follow-up response #2: A + AAAA
                transport.EnqueueResponse(BuildResponsePacket(
                    answers:
                    [
                        ARecord(host, IPAddress.Parse("198.51.100.77")),
                        AaaaRecord(host, IPAddress.Parse("2001:db8::77")),
                    ]));
            }
        };

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(120));

        BonjourServiceRecord record = Assert.Single(results);
        Assert.Equal((ushort)55000, record.Port);
        Assert.Equal(2, record.Endpoints.Count);

        var allQuestions = transport.SentQueries
            .Select(bytes =>
            {
                Assert.True(DnsWireCodec.TryDecodeMessage(bytes, out DnsMessage msg));
                return msg.Questions;
            })
            .SelectMany(q => q)
            .ToList();

        Assert.Contains(allQuestions, q => q.Type == DnsRecordType.PTR && q.Name.DisplayName == BonjourServiceTypes.RemotePairing);
        Assert.Contains(allQuestions, q => q.Type == DnsRecordType.SRV && q.Name.DisplayName == instance);
        Assert.Contains(allQuestions, q => q.Type == DnsRecordType.TXT && q.Name.DisplayName == instance);
        Assert.Contains(allQuestions, q => q.Type == DnsRecordType.A && q.Name.DisplayName == host);
        Assert.Contains(allQuestions, q => q.Type == DnsRecordType.AAAA && q.Name.DisplayName == host);
    }

    [Fact]
    public async Task HandlesRecordsWithAndWithoutTxtNameForManualPairingRemotePairingAndMobdev2()
    {
        var transport = new FakeMdnsTransport();
        // Manual pairing with TXT name
        const string manualWithName = "atv-1._remotepairing-manual-pairing._tcp.local.";
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairingManualPairing, manualWithName)],
            additional:
            [
                SrvRecord(manualWithName, "atv1.local.", 49153),
                TxtRecord(manualWithName, ("NAME", "Living Room Apple TV"), ("identifier", "id-atv1")),
                ARecord("atv1.local.", IPAddress.Parse("192.168.1.21")),
            ]));

        // Manual pairing missing "name" key
        const string manualWithoutName = "atv-2._remotepairing-manual-pairing._tcp.local.";
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairingManualPairing, manualWithoutName)],
            additional:
            [
                SrvRecord(manualWithoutName, "atv2.local.", 49154),
                TxtRecord(manualWithoutName, ("identifier", "id-atv2")),
                ARecord("atv2.local.", IPAddress.Parse("192.168.1.22")),
            ]));

        // Manual pairing with boolean "name" (no '=')
        const string manualBooleanName = "atv-3._remotepairing-manual-pairing._tcp.local.";
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairingManualPairing, manualBooleanName)],
            additional:
            [
                SrvRecord(manualBooleanName, "atv3.local.", 49155),
                TxtRawRecord(manualBooleanName, [0x04, (byte)'n', (byte)'a', (byte)'m', (byte)'e']),
                ARecord("atv3.local.", IPAddress.Parse("192.168.1.23")),
            ]));

        // Manual pairing with empty 0x00 TXT (omitted-TXT records are covered in dual-stack and filtering tests)
        const string manualEmptyTxt = "atv-4._remotepairing-manual-pairing._tcp.local.";
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairingManualPairing, manualEmptyTxt)],
            additional:
            [
                SrvRecord(manualEmptyTxt, "atv4.local.", 49156),
                TxtRawRecord(manualEmptyTxt, [0x00]),
                ARecord("atv4.local.", IPAddress.Parse("192.168.1.24")),
            ]));

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> manualResults =
            await client.BrowseRemotePairingManualPairingAsync(TimeSpan.FromMilliseconds(80));

        Assert.Equal(4, manualResults.Count);
        Assert.Equal("Living Room Apple TV", manualResults.Single(r => r.Instance == manualWithName).Name);
        Assert.Null(manualResults.Single(r => r.Instance == manualWithoutName).Name);
        Assert.Null(manualResults.Single(r => r.Instance == manualBooleanName).Name);
        Assert.Null(manualResults.Single(r => r.Instance == manualEmptyTxt).Name);

        // Mobdev2 partial coverage (advertised endpoints only)
        var mobdevTransport = new FakeMdnsTransport();
        const string mobdevInstance = "aa:bb:cc:dd:ee:ff@192.168.1.30._apple-mobdev2._tcp.local.";
        mobdevTransport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.Mobdev2, mobdevInstance)],
            additional:
            [
                SrvRecord(mobdevInstance, "phone.local.", 62078),
                TxtRecord(mobdevInstance, ("authTag", "tag=="), ("identifier", "udid-1")),
                ARecord("phone.local.", IPAddress.Parse("192.168.1.30")),
            ]));

        var mobdevClient = new BonjourClient(() => mobdevTransport);
        IReadOnlyList<BonjourServiceRecord> mobdevResults =
            await mobdevClient.BrowseMobdev2Async(TimeSpan.FromMilliseconds(80));

        BonjourServiceRecord mobdev = Assert.Single(mobdevResults);
        Assert.Equal(BonjourServiceTypes.Mobdev2, mobdev.ServiceType);
        Assert.Equal((ushort)62078, mobdev.Port);
        Assert.Null(mobdev.Name);
        Assert.Equal("192.168.1.30:62078", Assert.Single(mobdev.Endpoints).ToIPEndPoint().ToString());
    }

    [Fact]
    public async Task SupportsDualStackAndRetainsOffSubnetAddresses()
    {
        const string instance = "dual._remotepairing._tcp.local.";
        const string host = "dual-host.local.";

        var transport = new FakeMdnsTransport();
        transport.EnqueueResponse(
            BuildResponsePacket(
                answers: [PtrRecord(BonjourServiceTypes.RemotePairing, instance)],
                additional:
                [
                    SrvRecord(instance, host, 50001),
                    ARecord(host, IPAddress.Parse("198.51.100.42")), // non-local TEST-NET-2 IPv4 must NOT be filtered out
                    AaaaRecord(host, IPAddress.Parse("2001:db8:cafe::42")),
                    AaaaRecord(host, IPAddress.Parse("fe80::1234")),
                ]),
            interfaceIndex: 9,
            interfaceName: "Ethernet");

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(80));

        BonjourServiceRecord record = Assert.Single(results);
        Assert.Equal(3, record.Endpoints.Count);
        Assert.Equal(3, record.Addresses.Count);
        Assert.Contains(record.Endpoints, ep => ep.Address.Equals(IPAddress.Parse("198.51.100.42")));
        Assert.Contains(record.Endpoints, ep => ep.Address.Equals(IPAddress.Parse("2001:db8:cafe::42")));
        BonjourEndpoint linkLocal = Assert.Single(record.Endpoints, ep => ep.Address.IsIPv6LinkLocal);
        Assert.Equal(9L, linkLocal.Address.ScopeId);
        Assert.Equal("fe80::1234%9", linkLocal.FormattedAddress);
        Assert.Equal(9L, linkLocal.ToIPEndPoint().Address.ScopeId);
    }

    [Fact]
    public async Task MergesRepeatedMultiInterfaceBroadcastsWhileRetainingDistinctIPv6Scopes()
    {
        const string instance = "multi-nic._remotepairing._tcp.local.";
        const string host = "multi-nic.local.";
        IPAddress sharedIpv4 = IPAddress.Parse("192.168.10.25");
        IPAddress sharedLinkLocal = IPAddress.Parse("fe80::1ff:fe23:4567:890a");

        var transport = new FakeMdnsTransport();

        // Broadcast 1 on interface 12 ("Wi-Fi")
        transport.EnqueueResponse(
            BuildResponsePacket(
                answers: [PtrRecord(BonjourServiceTypes.RemotePairing, instance)],
                additional:
                [
                    SrvRecord(instance, host, 49999),
                    ARecord(host, sharedIpv4),
                    AaaaRecord(host, sharedLinkLocal),
                ]),
            interfaceIndex: 12,
            interfaceName: "Wi-Fi");

        // Broadcast 2 on interface 12 ("Wi-Fi") — exact duplicate
        transport.EnqueueResponse(
            BuildResponsePacket(
                answers: [PtrRecord(BonjourServiceTypes.RemotePairing, instance)],
                additional:
                [
                    SrvRecord(instance, host, 49999),
                    ARecord(host, sharedIpv4),
                    AaaaRecord(host, sharedLinkLocal),
                ]),
            interfaceIndex: 12,
            interfaceName: "Wi-Fi");

        // Broadcast 3 on interface 15 ("Ethernet") — same IPv4 (merged) + same fe80::... on NEW ScopeId 15 (retained!)
        transport.EnqueueResponse(
            BuildResponsePacket(
                answers: [PtrRecord(BonjourServiceTypes.RemotePairing, instance)],
                additional:
                [
                    SrvRecord(instance, host, 49999),
                    ARecord(host, sharedIpv4),
                    AaaaRecord(host, sharedLinkLocal),
                ]),
            interfaceIndex: 15,
            interfaceName: "Ethernet");

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(90));

        BonjourServiceRecord record = Assert.Single(results);
        Assert.Equal(3, record.Endpoints.Count);

        // Exactly 1 merged IPv4 endpoint
        BonjourEndpoint v4 = Assert.Single(record.Endpoints, ep => ep.Address.AddressFamily == AddressFamily.InterNetwork);
        Assert.Equal(sharedIpv4, v4.Address);

        // Exactly 2 distinct IPv6 link-local endpoints (ScopeId 12 and ScopeId 15)
        BonjourEndpoint[] v6Endpoints = record.Endpoints
            .Where(ep => ep.Address.AddressFamily == AddressFamily.InterNetworkV6)
            .OrderBy(ep => ep.Address.ScopeId)
            .ToArray();

        Assert.Equal(2, v6Endpoints.Length);
        Assert.Equal(12L, v6Endpoints[0].Address.ScopeId);
        Assert.Equal("fe80::1ff:fe23:4567:890a%12", v6Endpoints[0].FormattedAddress);
        Assert.Equal("Wi-Fi", v6Endpoints[0].InterfaceName);

        Assert.Equal(15L, v6Endpoints[1].Address.ScopeId);
        Assert.Equal("fe80::1ff:fe23:4567:890a%15", v6Endpoints[1].FormattedAddress);
        Assert.Equal("Ethernet", v6Endpoints[1].InterfaceName);
        Assert.Equal(15L, v6Endpoints[1].ToIPEndPoint().Address.ScopeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHostAndInstanceOnDifferentLinksKeepTheirOwnEndpointsAndTxt(bool samePort)
    {
        const string type = BonjourServiceTypes.RemotePairingManualPairing;
        const string instance = "phone." + type;
        const string host = "phone.local.";
        var transport = new FakeMdnsTransport();
        foreach (int link in new[] { 12, 15 })
        {
            transport.EnqueueResponse(BuildResponsePacket(answers: [
                PtrRecord(type, instance), SrvRecord(instance, host, (ushort)(samePort ? 5000 : 5000 + link)),
                TxtRecord(instance, ("name", $"phone-{link}")), ARecord(host, IPAddress.Parse($"192.0.2.{link}"))]), link);
        }

        var rows = await new BonjourClient(() => transport).BrowseRemotePairingManualPairingAsync(TimeSpan.FromMilliseconds(90));
        Assert.Equal(2, rows.Count);
        foreach (int link in new[] { 12, 15 })
        {
            var row = Assert.Single(rows, r => r.Name == $"phone-{link}");
            Assert.Equal(samePort ? 5000 : 5000 + link, (int)row.Port);
            Assert.Equal(IPAddress.Parse($"192.0.2.{link}"), Assert.Single(row.Endpoints).Address);
        }
    }

    [Fact]
    public void PublicEndpointUsesInterfaceIndexWhenLinkLocalAddressHasNoScope()
    {
        var endpoint = new BonjourEndpoint(IPAddress.Parse("fe80::10"), 1234, InterfaceIndex: 15);
        Assert.Equal(endpoint.FormattedAddress, endpoint.ToIPEndPoint().Address.ToString());
    }

    [Fact]
    public async Task WithdrawnPtrIsNotReturnedAtEndOfBrowse()
    {
        const string type = BonjourServiceTypes.RemotePairing;
        const string instance = "leaving." + type;
        var transport = new FakeMdnsTransport();
        transport.EnqueueResponse(BuildResponsePacket(answers: [PtrRecord(type, instance),
            SrvRecord(instance, "phone.local.", 5000), ARecord("phone.local.", IPAddress.Parse("192.0.2.10"))]));
        transport.EnqueueResponse(BuildResponsePacket(answers: [BuildRr(DnsWireName.Parse(type), DnsRecordType.PTR,
            1, 0, DnsWireCodec.EncodeName(DnsWireName.Parse(instance)))]));

        Assert.Empty(await new BonjourClient(() => transport).BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(90)));
    }

    [Fact]
    public async Task IgnoresIncomingQueriesAndUnrelatedServices()
    {
        const string wanted = "wanted._remotepairing._tcp.local.";
        const string spoofedFromQuery = "from-query._remotepairing._tcp.local.";
        const string unrelated = "speaker._airplay._tcp.local.";

        var transport = new FakeMdnsTransport();
        // QR=0 query containing known-answer section (must be ignored!)
        transport.EnqueueResponse(BuildMessagePacket(
            isResponse: false,
            answers:
            [
                PtrRecord(BonjourServiceTypes.RemotePairing, spoofedFromQuery),
                SrvRecord(spoofedFromQuery, "spoof.local.", 1234),
                ARecord("spoof.local.", IPAddress.Parse("192.0.2.99")),
            ]));

        // QR=1 response for unrelated service type
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord("_airplay._tcp.local.", unrelated)],
            additional:
            [
                SrvRecord(unrelated, "speaker.local.", 7000),
                ARecord("speaker.local.", IPAddress.Parse("192.168.1.99")),
            ]));

        // QR=1 response for wanted service
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairing, wanted)],
            additional:
            [
                SrvRecord(wanted, "wanted-host.local.", 49200),
                ARecord("wanted-host.local.", IPAddress.Parse("192.168.1.100")),
            ]));

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(80));

        BonjourServiceRecord single = Assert.Single(results);
        Assert.Equal(wanted, single.Instance);
    }

    [Fact]
    public async Task EmptyResultTimeoutReturnsEmptyAndPartialWindowReturnsCollectedRecords()
    {
        // Empty timeout
        var silentTransport = new FakeMdnsTransport();
        var client = new BonjourClient(() => silentTransport);

        IReadOnlyList<BonjourServiceRecord> empty = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(60));
        Assert.Empty(empty);
        Assert.True(silentTransport.IsDisposed);

        // Partial window timeout: packet 1 arrives immediately, packet 2 is scheduled well after timeout
        var partialTransport = new FakeMdnsTransport();
        const string earlyInstance = "early._remotepairing._tcp.local.";
        const string lateInstance = "late._remotepairing._tcp.local.";

        partialTransport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairing, earlyInstance)],
            additional:
            [
                SrvRecord(earlyInstance, "early.local.", 49100),
                ARecord("early.local.", IPAddress.Parse("192.168.1.10")),
            ]));

        using var lateTimer = new Timer(_ =>
        {
            partialTransport.EnqueueResponse(BuildResponsePacket(
                answers: [PtrRecord(BonjourServiceTypes.RemotePairing, lateInstance)],
                additional:
                [
                    SrvRecord(lateInstance, "late.local.", 49200),
                    ARecord("late.local.", IPAddress.Parse("192.168.1.11")),
                ]));
        }, null, 600, Timeout.Infinite);

        var partialClient = new BonjourClient(() => partialTransport);
        IReadOnlyList<BonjourServiceRecord> partial =
            await partialClient.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(90));

        BonjourServiceRecord record = Assert.Single(partial);
        Assert.Equal(earlyInstance, record.Instance);
        Assert.True(partialTransport.IsDisposed);
    }

    [Fact]
    public async Task ActiveCallerCancellationExitsPromptlyAndDisposesTransport()
    {
        var transport = new FakeMdnsTransport();
        var client = new BonjourClient(() => transport);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.BrowseRemotePairingAsync(TimeSpan.FromSeconds(10), cts.Token));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Cancellation took too long: {stopwatch.Elapsed}");
        Assert.True(transport.IsDisposed);

        // Pre-cancelled token must throw without creating transport
        bool factoryCalled = false;
        var preCancelledClient = new BonjourClient(() =>
        {
            factoryCalled = true;
            return new FakeMdnsTransport();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            preCancelledClient.BrowseRemotePairingAsync(cancellationToken: new CancellationToken(canceled: true)));
        Assert.False(factoryCalled);
    }

    [Fact]
    public async Task BrowsesMultipleServiceTypesAndValidatesArguments()
    {
        var transport = new FakeMdnsTransport();
        const string rpInstance = "rp._remotepairing._tcp.local.";
        const string mpInstance = "mp._remotepairing-manual-pairing._tcp.local.";

        transport.EnqueueResponse(BuildResponsePacket(
            answers:
            [
                PtrRecord(BonjourServiceTypes.RemotePairing, rpInstance),
                PtrRecord(BonjourServiceTypes.RemotePairingManualPairing, mpInstance),
            ],
            additional:
            [
                SrvRecord(rpInstance, "ios.local.", 50100),
                SrvRecord(mpInstance, "ios.local.", 50101),
                TxtRecord(mpInstance, ("name", "Test Phone")),
                ARecord("ios.local.", IPAddress.Parse("192.168.1.55")),
            ]));

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseAsync(
            [BonjourServiceTypes.RemotePairing, BonjourServiceTypes.RemotePairingManualPairing],
            TimeSpan.FromMilliseconds(80));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ServiceType == BonjourServiceTypes.RemotePairing && r.Name is null);
        Assert.Contains(results, r => r.ServiceType == BonjourServiceTypes.RemotePairingManualPairing && r.Name == "Test Phone");

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.BrowseAsync((string)null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.BrowseAsync("   "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.BrowseAsync((IReadOnlyCollection<string>)null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.BrowseAsync(Array.Empty<string>()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.BrowseRemotePairingAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task ResolvesSplitPacketInstancesContainingDotsAndDoubleDotsWithoutCrashOrCorruption()
    {
        var serviceWireName = DnsWireName.Parse(BonjourServiceTypes.RemotePairingManualPairing);
        var doubleDotInstance = new DnsWireName(["Dr..Phone", "_remotepairing-manual-pairing", "_tcp", "local"]);
        var singleDotInstance = new DnsWireName(["iPhone v1.2", "_remotepairing-manual-pairing", "_tcp", "local"]);
        var doubleDotHost = new DnsWireName(["dr-phone", "local"]);
        var singleDotHost = new DnsWireName(["iphone-v12", "local"]);

        var transport = new FakeMdnsTransport();
        transport.OnQuerySent = qPacket =>
        {
            Assert.True(DnsWireCodec.TryDecodeMessage(qPacket, out DnsMessage query));
            if (query.Questions.Any(q => q.Type == DnsRecordType.PTR))
            {
                // PTR-only split packets without inline SRV/TXT (reproduces G1.7 and E1)
                transport.EnqueueResponse(BuildResponsePacket(
                    answers: [PtrRecord(serviceWireName, doubleDotInstance)]));
                transport.EnqueueResponse(BuildResponsePacket(
                    answers: [PtrRecord(serviceWireName, singleDotInstance)]));
                return;
            }

            foreach (DnsQuestion q in query.Questions)
            {
                if (q.Type == DnsRecordType.SRV && q.Name.Equals(doubleDotInstance))
                {
                    transport.EnqueueResponse(BuildResponsePacket(
                        answers:
                        [
                            SrvRecord(doubleDotInstance, doubleDotHost, 50101),
                            TxtRecord(doubleDotInstance, ("name", "Dr..Phone")),
                            ARecord(doubleDotHost, IPAddress.Parse("192.168.10.101")),
                        ]));
                }
                else if (q.Type == DnsRecordType.SRV && q.Name.Equals(singleDotInstance))
                {
                    transport.EnqueueResponse(BuildResponsePacket(
                        answers:
                        [
                            SrvRecord(singleDotInstance, singleDotHost, 50102),
                            TxtRecord(singleDotInstance, ("name", "iPhone v1.2")),
                            ARecord(singleDotHost, IPAddress.Parse("192.168.10.102")),
                        ]));
                }
            }
        };

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results =
            await client.BrowseRemotePairingManualPairingAsync(TimeSpan.FromMilliseconds(150));

        Assert.Equal(2, results.Count);

        BonjourServiceRecord drPhone = Assert.Single(results, r => r.Name == "Dr..Phone");
        Assert.Equal("Dr..Phone._remotepairing-manual-pairing._tcp.local.", drPhone.Instance);
        Assert.Equal((ushort)50101, drPhone.Port);
        Assert.Equal("192.168.10.101:50101", Assert.Single(drPhone.Endpoints).ToIPEndPoint().ToString());

        BonjourServiceRecord iPhoneV12 = Assert.Single(results, r => r.Name == "iPhone v1.2");
        Assert.Equal("iPhone v1.2._remotepairing-manual-pairing._tcp.local.", iPhoneV12.Instance);
        Assert.Equal((ushort)50102, iPhoneV12.Port);
        Assert.Equal("192.168.10.102:50102", Assert.Single(iPhoneV12.Endpoints).ToIPEndPoint().ToString());

        // Verify emitted follow-up wire queries preserved single-label boundaries (offset 12 label length = 9 and 11).
        var followUpQueries = transport.SentQueries.Skip(1).ToList();
        Assert.NotEmpty(followUpQueries);

        var decodedFollowUps = followUpQueries
            .Select(bytes =>
            {
                Assert.True(DnsWireCodec.TryDecodeMessage(bytes, out DnsMessage msg));
                return (Bytes: bytes, Message: msg);
            })
            .ToList();

        var drFollowUp = decodedFollowUps.First(x =>
            x.Message.Questions.Any(q => q.Type == DnsRecordType.SRV && q.Name.Equals(doubleDotInstance)));
        Assert.Equal((byte)Encoding.UTF8.GetByteCount("Dr..Phone"), drFollowUp.Bytes[12]);
        Assert.All(
            drFollowUp.Message.Questions.Where(q => q.Name.Equals(doubleDotInstance)),
            q => Assert.Equal(["Dr..Phone", "_remotepairing-manual-pairing", "_tcp", "local"], q.Name.Labels));

        var v12FollowUp = decodedFollowUps.First(x =>
            x.Message.Questions.Any(q => q.Type == DnsRecordType.SRV && q.Name.Equals(singleDotInstance)));
        Assert.Equal((byte)Encoding.UTF8.GetByteCount("iPhone v1.2"), v12FollowUp.Bytes[12]);
        Assert.All(
            v12FollowUp.Message.Questions.Where(q => q.Name.Equals(singleDotInstance)),
            q => Assert.Equal(["iPhone v1.2", "_remotepairing-manual-pairing", "_tcp", "local"], q.Name.Labels));
    }

    [Fact]
    public async Task DistinguishesDotInsideSingleLabelFromMultiLabelNamesAndDeduplicatesFollowUpsCaseInsensitively()
    {
        var serviceWireName = DnsWireName.Parse(BonjourServiceTypes.RemotePairing);
        var singleLabelDot = new DnsWireName(["a.b", "_remotepairing", "_tcp", "local"]);
        var twoLabels = new DnsWireName(["a", "b", "_remotepairing", "_tcp", "local"]);
        var upperHost = new DnsWireName(["SharedHost", "local"]);
        var lowerHost = new DnsWireName(["sharedhost", "local"]);

        var transport = new FakeMdnsTransport();
        transport.EnqueueResponse(BuildResponsePacket(
            answers:
            [
                PtrRecord(serviceWireName, singleLabelDot),
                PtrRecord(serviceWireName, twoLabels),
            ],
            additional:
            [
                SrvRecord(singleLabelDot, upperHost, 49152),
                SrvRecord(twoLabels, lowerHost, 49153),
            ]));

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(80));

        // Even though DisplayName of both is "a.b._remotepairing._tcp.local.", label-sequence keys keep their ports separate.
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Port == 49152);
        Assert.Contains(results, r => r.Port == 49153);

        // Case-insensitive sentFollowUpQueries must emit only ONE A and ONE AAAA query for SharedHost.local. / sharedhost.local.
        var allQuestions = transport.SentQueries
            .SelectMany(bytes =>
            {
                Assert.True(DnsWireCodec.TryDecodeMessage(bytes, out DnsMessage msg));
                return msg.Questions;
            })
            .ToList();

        Assert.Single(allQuestions, q => q.Type == DnsRecordType.A && q.Name.Equals(lowerHost));
        Assert.Single(allQuestions, q => q.Type == DnsRecordType.AAAA && q.Name.Equals(lowerHost));
    }

    [Fact]
    public async Task IgnoresNonZeroRcodeResponsePackets()
    {
        const string rejectedInstance = "nxdomain._remotepairing._tcp.local.";
        const string validInstance = "valid._remotepairing._tcp.local.";

        var transport = new FakeMdnsTransport();
        // RCODE = 3 (NXDOMAIN) and RCODE = 5 (REFUSED) responses must be ignored per RFC 6762 §18.3.
        transport.EnqueueResponse(BuildMessagePacket(
            isResponse: true,
            rcode: 3,
            answers: [PtrRecord(BonjourServiceTypes.RemotePairing, rejectedInstance)],
            additional:
            [
                SrvRecord(rejectedInstance, "bad.local.", 49100),
                ARecord("bad.local.", IPAddress.Parse("192.0.2.10")),
            ]));

        transport.EnqueueResponse(BuildMessagePacket(
            isResponse: true,
            rcode: 5,
            answers: [PtrRecord(BonjourServiceTypes.RemotePairing, "refused._remotepairing._tcp.local.")],
            additional:
            [
                SrvRecord("refused._remotepairing._tcp.local.", "refused.local.", 49101),
                ARecord("refused.local.", IPAddress.Parse("192.0.2.11")),
            ]));

        // RCODE = 0 valid response
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairing, validInstance)],
            additional:
            [
                SrvRecord(validInstance, "good.local.", 49102),
                ARecord("good.local.", IPAddress.Parse("192.168.1.50")),
            ]));

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(80));

        BonjourServiceRecord record = Assert.Single(results);
        Assert.Equal(validInstance, record.Instance);
        Assert.Equal((ushort)49102, record.Port);
    }

    [Fact]
    public async Task IgnoresUnencodableFollowUpNamesFromMalformedRemotePackets()
    {
        // Construct a PTR response whose target contains a 63-byte 0xFF invalid-UTF-8 label (expands to 189 UTF-8 bytes)
        var rrBuf = new List<byte>();
        rrBuf.AddRange(DnsWireCodec.EncodeName(BonjourServiceTypes.RemotePairing));
        WriteUInt16(rrBuf, (ushort)DnsRecordType.PTR);
        WriteUInt16(rrBuf, 0x0001);
        WriteUInt32(rrBuf, 120);
        byte[] suffix = DnsWireCodec.EncodeName("_remotepairing._tcp.local.");
        WriteUInt16(rrBuf, (ushort)(1 + 63 + suffix.Length));
        rrBuf.Add(63);
        rrBuf.AddRange(Enumerable.Repeat((byte)0xFF, 63));
        rrBuf.AddRange(suffix);

        const string validInstance = "healthy._remotepairing._tcp.local.";
        var transport = new FakeMdnsTransport();
        transport.EnqueueResponse(BuildResponsePacket(answers: [rrBuf.ToArray()]));
        transport.EnqueueResponse(BuildResponsePacket(
            answers: [PtrRecord(BonjourServiceTypes.RemotePairing, validInstance)],
            additional:
            [
                SrvRecord(validInstance, "healthy.local.", 49222),
                ARecord("healthy.local.", IPAddress.Parse("192.168.1.88")),
            ]));

        var client = new BonjourClient(() => transport);
        IReadOnlyList<BonjourServiceRecord> results = await client.BrowseRemotePairingAsync(TimeSpan.FromMilliseconds(80));

        BonjourServiceRecord record = Assert.Single(results);
        Assert.Equal(validInstance, record.Instance);
    }

    [Fact]
    public async Task RejectsOversizedTimeoutWithTimeoutParamName()
    {
        var client = new BonjourClient(() => new FakeMdnsTransport());

        ArgumentOutOfRangeException exDays = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.BrowseRemotePairingAsync(TimeSpan.FromDays(60)));
        Assert.Equal("timeout", exDays.ParamName);

        ArgumentOutOfRangeException exBoundary = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.BrowseAsync([BonjourServiceTypes.RemotePairing], TimeSpan.FromMilliseconds(uint.MaxValue)));
        Assert.Equal("timeout", exBoundary.ParamName);
    }

    private sealed class FakeMdnsTransport : IMdnsTransport
    {
        private readonly Channel<MdnsReceivedPacket> channel = Channel.CreateUnbounded<MdnsReceivedPacket>();

        public List<byte[]> SentQueries { get; } = [];
        public Action<byte[]>? OnQuerySent { get; set; }
        public bool IsDisposed { get; private set; }

        public void EnqueueResponse(byte[] payload, int interfaceIndex = 1, string? interfaceName = "eth0")
        {
            channel.Writer.TryWrite(new MdnsReceivedPacket(payload, interfaceIndex, interfaceName));
        }

        public Task SendQueryAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] copy = packet.ToArray();
            SentQueries.Add(copy);
            OnQuerySent?.Invoke(copy);
            return Task.CompletedTask;
        }

        public async ValueTask<MdnsReceivedPacket?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (channel.Reader.TryRead(out MdnsReceivedPacket packet))
                {
                    return packet;
                }
            }

            return null;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private static byte[] BuildResponsePacket(
        IReadOnlyList<byte[]>? answers = null,
        IReadOnlyList<byte[]>? authorities = null,
        IReadOnlyList<byte[]>? additional = null)
        => BuildMessagePacket(isResponse: true, answers, authorities, additional);

    private static byte[] BuildMessagePacket(
        bool isResponse,
        IReadOnlyList<byte[]>? answers = null,
        IReadOnlyList<byte[]>? authorities = null,
        IReadOnlyList<byte[]>? additional = null,
        ushort rcode = 0)
    {
        answers ??= [];
        authorities ??= [];
        additional ??= [];

        var buf = new List<byte>(128);
        WriteUInt16(buf, 0); // TXID
        ushort flags = isResponse ? (ushort)(0x8400 | (rcode & 0x000F)) : (ushort)(rcode & 0x000F);
        WriteUInt16(buf, flags);
        WriteUInt16(buf, 0); // QDCOUNT
        WriteUInt16(buf, (ushort)answers.Count);
        WriteUInt16(buf, (ushort)authorities.Count);
        WriteUInt16(buf, (ushort)additional.Count);

        foreach (byte[] rr in answers) buf.AddRange(rr);
        foreach (byte[] rr in authorities) buf.AddRange(rr);
        foreach (byte[] rr in additional) buf.AddRange(rr);

        return buf.ToArray();
    }

    private static byte[] PtrRecord(string serviceType, string instance)
        => PtrRecord(DnsWireName.Parse(serviceType), DnsWireName.Parse(instance));

    private static byte[] PtrRecord(DnsWireName serviceType, DnsWireName instance)
    {
        byte[] rdata = DnsWireCodec.EncodeName(instance);
        return BuildRr(serviceType, DnsRecordType.PTR, 0x0001, 120, rdata);
    }

    private static byte[] SrvRecord(string instance, string targetHost, ushort port)
        => SrvRecord(DnsWireName.Parse(instance), DnsWireName.Parse(targetHost), port);

    private static byte[] SrvRecord(DnsWireName instance, DnsWireName targetHost, ushort port)
    {
        var rdata = new List<byte>();
        WriteUInt16(rdata, 0);
        WriteUInt16(rdata, 0);
        WriteUInt16(rdata, port);
        rdata.AddRange(DnsWireCodec.EncodeName(targetHost));
        return BuildRr(instance, DnsRecordType.SRV, 0x8001, 120, rdata.ToArray());
    }

    private static byte[] TxtRecord(string instance, params (string Key, string? Value)[] entries)
        => TxtRecord(DnsWireName.Parse(instance), entries);

    private static byte[] TxtRecord(DnsWireName instance, params (string Key, string? Value)[] entries)
    {
        var rdata = new List<byte>();
        foreach ((string key, string? value) in entries)
        {
            string entry = value is null ? key : $"{key}={value}";
            byte[] utf8 = Encoding.UTF8.GetBytes(entry);
            rdata.Add((byte)utf8.Length);
            rdata.AddRange(utf8);
        }

        if (rdata.Count == 0)
        {
            rdata.Add(0);
        }

        return BuildRr(instance, DnsRecordType.TXT, 0x8001, 4500, rdata.ToArray());
    }

    private static byte[] TxtRawRecord(string instance, byte[] rdata)
        => BuildRr(DnsWireName.Parse(instance), DnsRecordType.TXT, 0x8001, 4500, rdata);

    private static byte[] ARecord(string host, IPAddress ipv4)
        => ARecord(DnsWireName.Parse(host), ipv4);

    private static byte[] ARecord(DnsWireName host, IPAddress ipv4)
        => BuildRr(host, DnsRecordType.A, 0x8001, 120, ipv4.GetAddressBytes());

    private static byte[] AaaaRecord(string host, IPAddress ipv6)
        => BuildRr(DnsWireName.Parse(host), DnsRecordType.AAAA, 0x8001, 120, ipv6.GetAddressBytes());

    private static byte[] BuildRr(DnsWireName name, DnsRecordType type, ushort rclass, uint ttl, byte[] rdata)
    {
        byte[] encodedName = DnsWireCodec.EncodeName(name);
        var buf = new List<byte>(encodedName.Length + rdata.Length + 16);
        buf.AddRange(encodedName);
        WriteUInt16(buf, (ushort)type);
        WriteUInt16(buf, rclass);
        WriteUInt32(buf, ttl);
        WriteUInt16(buf, (ushort)rdata.Length);
        buf.AddRange(rdata);
        return buf.ToArray();
    }

    private static void WriteUInt16(List<byte> buf, ushort val)
    {
        buf.Add((byte)(val >> 8));
        buf.Add((byte)(val & 0xFF));
    }

    private static void WriteUInt32(List<byte> buf, uint val)
    {
        buf.Add((byte)(val >> 24));
        buf.Add((byte)((val >> 16) & 0xFF));
        buf.Add((byte)((val >> 8) & 0xFF));
        buf.Add((byte)(val & 0xFF));
    }
}
