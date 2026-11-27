namespace iMobileDevice.Managed.Bonjour;

using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

internal sealed class BonjourDiscoveryAccumulator
{
    private readonly Dictionary<DnsWireName, string> queriedServices; // wire name -> canonical ServiceType
    private readonly Dictionary<string, HashSet<DnsWireName>> ptrInstancesByService = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DnsWireName, DnsWireName> canonicalInstanceNames = new();
    private readonly Dictionary<DnsWireName, List<SrvEntry>> srvByInstance = new();
    private readonly Dictionary<DnsWireName, Dictionary<string, string?>> txtByInstance = new();
    private readonly Dictionary<DnsWireName, List<ResolvedAddressEntry>> addressesByHost = new();
    private readonly HashSet<(DnsWireName Name, DnsRecordType Type)> sentFollowUpQueries = new();

    public BonjourDiscoveryAccumulator(IEnumerable<string> serviceTypes)
    {
        ArgumentNullException.ThrowIfNull(serviceTypes);

        queriedServices = new Dictionary<DnsWireName, string>();
        foreach (string serviceType in serviceTypes)
        {
            string normalized = BonjourServiceTypes.Normalize(serviceType);
            queriedServices.TryAdd(DnsWireName.Parse(normalized), normalized);
        }

        if (queriedServices.Count == 0)
        {
            throw new ArgumentException("At least one service type must be provided.", nameof(serviceTypes));
        }
    }

    public bool AddPacket(MdnsReceivedPacket packet)
    {
        if (!DnsWireCodec.TryDecodeMessage(packet.Payload.Span, out DnsMessage message) ||
            !message.IsResponse ||
            message.ResponseCode != 0)
        {
            return false;
        }

        ProcessRecords(message.Answers, packet);
        ProcessRecords(message.Authorities, packet);
        ProcessRecords(message.Additional, packet);
        return true;
    }

    public IReadOnlyList<DnsQuestion> TakePendingFollowUpQuestions()
    {
        var questions = new List<DnsQuestion>();

        foreach (string serviceKey in queriedServices.Values)
        {
            if (!ptrInstancesByService.TryGetValue(serviceKey, out HashSet<DnsWireName>? instances))
            {
                continue;
            }

            foreach (DnsWireName instance in instances)
            {
                if (!txtByInstance.ContainsKey(instance) &&
                    sentFollowUpQueries.Add((instance, DnsRecordType.TXT)))
                {
                    questions.Add(new DnsQuestion(instance, DnsRecordType.TXT));
                }

                if (!srvByInstance.TryGetValue(instance, out List<SrvEntry>? srvs) || srvs.Count == 0)
                {
                    if (sentFollowUpQueries.Add((instance, DnsRecordType.SRV)))
                    {
                        questions.Add(new DnsQuestion(instance, DnsRecordType.SRV));
                    }

                    continue;
                }

                foreach (SrvEntry srv in srvs)
                {
                    if (srv.TargetHost.IsRoot)
                    {
                        continue;
                    }

                    bool hasIpv4 = false;
                    bool hasIpv6 = false;
                    if (addressesByHost.TryGetValue(srv.TargetHost, out List<ResolvedAddressEntry>? hostAddrs))
                    {
                        foreach (ResolvedAddressEntry addr in hostAddrs)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                hasIpv4 = true;
                            }
                            else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                hasIpv6 = true;
                            }
                        }
                    }

                    if (!hasIpv4 && sentFollowUpQueries.Add((srv.TargetHost, DnsRecordType.A)))
                    {
                        questions.Add(new DnsQuestion(srv.TargetHost, DnsRecordType.A));
                    }

                    if (!hasIpv6 && sentFollowUpQueries.Add((srv.TargetHost, DnsRecordType.AAAA)))
                    {
                        questions.Add(new DnsQuestion(srv.TargetHost, DnsRecordType.AAAA));
                    }
                }
            }
        }

        return questions;
    }

    public IReadOnlyList<BonjourServiceRecord> BuildResults()
    {
        var results = new List<BonjourServiceRecord>();

        foreach (string serviceType in queriedServices.Values)
        {
            if (!ptrInstancesByService.TryGetValue(serviceType, out HashSet<DnsWireName>? instances))
            {
                continue;
            }

            foreach (DnsWireName instanceKey in instances)
            {
                if (!srvByInstance.TryGetValue(instanceKey, out List<SrvEntry>? srvs) || srvs.Count == 0)
                {
                    continue;
                }

                string instance = (canonicalInstanceNames.TryGetValue(instanceKey, out DnsWireName? savedInstance)
                    ? savedInstance
                    : instanceKey).DisplayName;

                IReadOnlyDictionary<string, string?> txt = txtByInstance.TryGetValue(instanceKey, out Dictionary<string, string?>? txtMap)
                    ? new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(txtMap, StringComparer.OrdinalIgnoreCase))
                    : ReadOnlyDictionary<string, string?>.Empty;

                string? name = txt.TryGetValue("name", out string? rawName) ? rawName : null;

                foreach (SrvEntry srv in srvs)
                {
                    string hostName = srv.TargetHost.IsRoot
                        ? string.Empty
                        : srv.TargetHost.StripTrailingDot();

                    var endpoints = new List<BonjourEndpoint>();
                    var seenEndpoints = new HashSet<EndpointDedupKey>();

                    if (addressesByHost.TryGetValue(srv.TargetHost, out List<ResolvedAddressEntry>? hostAddresses))
                    {
                        foreach (ResolvedAddressEntry addr in hostAddresses)
                        {
                            long scopeId = addr.Address.IsIPv6LinkLocal ? addr.Address.ScopeId : 0;
                            var dedupKey = new EndpointDedupKey(addr.Address, scopeId, srv.Port);
                            if (seenEndpoints.Add(dedupKey))
                            {
                                endpoints.Add(new BonjourEndpoint(addr.Address, srv.Port, addr.InterfaceIndex, addr.InterfaceName));
                            }
                        }
                    }

                    results.Add(new BonjourServiceRecord(
                        serviceType,
                        instance,
                        hostName,
                        srv.Port,
                        endpoints,
                        txt,
                        name));
                }
            }
        }

        results.Sort(static (left, right) =>
        {
            int cmp = StringComparer.OrdinalIgnoreCase.Compare(left.ServiceType, right.ServiceType);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(left.Instance, right.Instance);
            if (cmp != 0) return cmp;
            cmp = StringComparer.OrdinalIgnoreCase.Compare(left.HostName, right.HostName);
            if (cmp != 0) return cmp;
            return left.Port.CompareTo(right.Port);
        });

        return results;
    }

    private void ProcessRecords(IReadOnlyList<DnsResourceRecord> records, MdnsReceivedPacket packet)
    {
        foreach (DnsResourceRecord rr in records)
        {
            DnsWireName rrName = rr.Name;
            switch (rr.Type)
            {
                case DnsRecordType.PTR when rr.TargetName is not null:
                {
                    if (queriedServices.TryGetValue(rrName, out string? canonicalService))
                    {
                        DnsWireName instanceName = rr.TargetName;
                        canonicalInstanceNames.TryAdd(instanceName, instanceName);

                        if (!ptrInstancesByService.TryGetValue(canonicalService, out HashSet<DnsWireName>? set))
                        {
                            set = new HashSet<DnsWireName>();
                            ptrInstancesByService[canonicalService] = set;
                        }

                        set.Add(instanceName);
                    }

                    break;
                }

                case DnsRecordType.SRV when rr.TargetName is not null && rr.Port is not null:
                {
                    canonicalInstanceNames.TryAdd(rrName, rrName);
                    if (!srvByInstance.TryGetValue(rrName, out List<SrvEntry>? list))
                    {
                        list = [];
                        srvByInstance[rrName] = list;
                    }

                    var entry = new SrvEntry(rr.TargetName, rr.Port.Value);
                    if (!list.Contains(entry))
                    {
                        list.Add(entry);
                    }

                    break;
                }

                case DnsRecordType.TXT when rr.TxtRecords is not null:
                {
                    canonicalInstanceNames.TryAdd(rrName, rrName);
                    if (!txtByInstance.TryGetValue(rrName, out Dictionary<string, string?>? txtMap))
                    {
                        txtMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        txtByInstance[rrName] = txtMap;
                    }

                    foreach (KeyValuePair<string, string?> kv in rr.TxtRecords)
                    {
                        txtMap[kv.Key] = kv.Value;
                    }

                    break;
                }

                case DnsRecordType.A or DnsRecordType.AAAA when rr.Address is not null:
                {
                    IPAddress addr = rr.Address;
                    if (addr.IsIPv6LinkLocal && addr.ScopeId == 0 && packet.InterfaceIndex > 0)
                    {
                        addr = new IPAddress(addr.GetAddressBytes(), packet.InterfaceIndex);
                    }

                    int? ifIndex = packet.InterfaceIndex > 0
                        ? packet.InterfaceIndex
                        : (addr.IsIPv6LinkLocal && addr.ScopeId > 0 ? (int)addr.ScopeId : null);

                    if (!addressesByHost.TryGetValue(rrName, out List<ResolvedAddressEntry>? hostAddrs))
                    {
                        hostAddrs = [];
                        addressesByHost[rrName] = hostAddrs;
                    }

                    long scopeId = addr.IsIPv6LinkLocal ? addr.ScopeId : 0;
                    int existingIndex = -1;
                    for (int i = 0; i < hostAddrs.Count; i++)
                    {
                        long existingScope = hostAddrs[i].Address.IsIPv6LinkLocal ? hostAddrs[i].Address.ScopeId : 0;
                        if (hostAddrs[i].Address.Equals(addr) && existingScope == scopeId)
                        {
                            existingIndex = i;
                            break;
                        }
                    }

                    if (existingIndex < 0)
                    {
                        hostAddrs.Add(new ResolvedAddressEntry(addr, ifIndex, packet.InterfaceName));
                    }
                    else if (hostAddrs[existingIndex].InterfaceName is null && packet.InterfaceName is not null)
                    {
                        ResolvedAddressEntry prev = hostAddrs[existingIndex];
                        hostAddrs[existingIndex] = prev with
                        {
                            InterfaceIndex = prev.InterfaceIndex ?? ifIndex,
                            InterfaceName = packet.InterfaceName,
                        };
                    }

                    break;
                }
            }
        }
    }

    private readonly record struct SrvEntry(DnsWireName TargetHost, ushort Port);

    private readonly record struct ResolvedAddressEntry(IPAddress Address, int? InterfaceIndex, string? InterfaceName);

    private readonly record struct EndpointDedupKey(IPAddress Address, long ScopeId, ushort Port);
}
