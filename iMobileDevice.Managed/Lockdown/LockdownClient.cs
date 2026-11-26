namespace iMobileDevice.Managed.Lockdown;

using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Usbmuxd;

public sealed record LockdownServiceDescriptor(
    string ServiceName,
    ushort Port,
    bool EnableServiceSsl);

public sealed class LockdownException : Exception
{
    public LockdownException(string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}

/// <summary>
/// Pure managed client for Apple's <c>lockdownd</c> service (port 62078), supporting pairing, TLS sessions,
/// device property queries, and starting downstream device services.
/// </summary>
public sealed class LockdownClient : IAsyncDisposable
{
    public const ushort LockdownPort = 62078;

    private readonly UsbmuxdClient usbmuxd;
    private readonly UsbmuxdDeviceInfo device;
    private readonly string label;
    private readonly Stream baseStream;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private Stream transport;
    private X509Certificate2? sessionCert;
    private bool transportFaulted;
    private bool disposed;

    private LockdownClient(UsbmuxdClient usbmuxd, UsbmuxdDeviceInfo device, Stream transport, string label)
    {
        this.usbmuxd = usbmuxd;
        this.device = device;
        this.baseStream = transport;
        this.transport = transport;
        this.label = label;
    }

    public UsbmuxdDeviceInfo Device => device;
    public PairRecord? PairRecord { get; private set; }
    public string? SessionId { get; private set; }
    public bool IsSslEnabled => transport is SslStream;

    /// <summary>
    /// Connects to <c>lockdownd</c> on the specified device and optionally establishes a paired TLS session.
    /// </summary>
    public static async Task<LockdownClient> ConnectAsync(
        UsbmuxdClient usbmuxd,
        UsbmuxdDeviceInfo device,
        bool handshake = true,
        bool autoPair = false,
        string label = "iMobileDevice.Managed",
        PairRecord? pairRecord = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usbmuxd);
        ArgumentNullException.ThrowIfNull(device);

        Stream stream = await usbmuxd.ConnectAsync(device.DeviceId, LockdownPort, cancellationToken).ConfigureAwait(false);
        var client = new LockdownClient(usbmuxd, device, stream, label);

        try
        {
            if (handshake)
            {
                string type = await client.QueryTypeAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(type, "com.apple.mobile.lockdown", StringComparison.Ordinal))
                {
                    throw new LockdownException($"Unexpected lockdown service type: '{type}'.");
                }

                PairRecord? record = pairRecord ?? await usbmuxd.ReadPairRecordAsync(device.SerialNumber, cancellationToken).ConfigureAwait(false);
                if (record == null)
                {
                    if (!autoPair)
                    {
                        throw new LockdownException($"No pair record found for device '{device.SerialNumber}'. Enable autoPair or pair the device first.", "InvalidHostID");
                    }

                    record = await client.PairAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await client.StartSessionAsync(record, cancellationToken).ConfigureAwait(false);
            }

            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<LockdownClient> ConnectFirstDeviceAsync(
        UsbmuxdClient? usbmuxd = null,
        bool handshake = true,
        bool autoPair = false,
        string label = "iMobileDevice.Managed",
        CancellationToken cancellationToken = default)
    {
        usbmuxd ??= new UsbmuxdClient();
        IReadOnlyList<UsbmuxdDeviceInfo> devices = await usbmuxd.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        UsbmuxdDeviceInfo? target = devices.FirstOrDefault(d => d.ConnectionType == UsbmuxdConnectionType.Usb) ?? devices.FirstOrDefault();
        if (target == null)
        {
            throw new LockdownException("No iOS device detected via usbmuxd.");
        }

        return await ConnectAsync(usbmuxd, target, handshake, autoPair, label, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> QueryTypeAsync(CancellationToken cancellationToken = default)
    {
        PlistDictionary reply = await RequestAsync(new PlistDictionary { ["Request"] = "QueryType" }, cancellationToken).ConfigureAwait(false);
        return reply.GetString("Type") ?? throw new LockdownException("QueryType response did not contain Type.");
    }

    public async Task<PlistNode?> GetValueAsync(string? domain = null, string? key = null, CancellationToken cancellationToken = default)
    {
        var req = new PlistDictionary { ["Request"] = "GetValue" };
        if (!string.IsNullOrEmpty(domain))
        {
            req["Domain"] = domain;
        }

        if (!string.IsNullOrEmpty(key))
        {
            req["Key"] = key;
        }

        PlistDictionary reply = await RequestAsync(req, cancellationToken).ConfigureAwait(false);
        return reply.TryGetValue("Value", out PlistNode? val) ? val : null;
    }

    public async Task<string?> GetStringValueAsync(string key, string? domain = null, CancellationToken cancellationToken = default)
    {
        PlistNode? node = await GetValueAsync(domain, key, cancellationToken).ConfigureAwait(false);
        return node is PlistString s ? s.Value : node?.ToString();
    }

    public async Task<PlistDictionary> GetAllValuesAsync(string? domain = null, CancellationToken cancellationToken = default)
    {
        PlistNode? node = await GetValueAsync(domain, null, cancellationToken).ConfigureAwait(false);
        return node as PlistDictionary ?? new PlistDictionary();
    }

    public async Task<PairRecord> PairAsync(
        TimeSpan? trustPromptTimeout = null,
        bool saveToUsbmuxd = true,
        CancellationToken cancellationToken = default)
    {
        byte[]? devicePubKey = (await GetValueAsync(null, "DevicePublicKey", cancellationToken).ConfigureAwait(false) as PlistData)?.Value;
        if (devicePubKey == null || devicePubKey.Length == 0)
        {
            throw new LockdownException("Could not retrieve DevicePublicKey from device.");
        }

        string? wifiMac = await GetStringValueAsync("WiFiAddress", null, cancellationToken).ConfigureAwait(false);
        string systemBuid = await usbmuxd.ReadBuidAsync(cancellationToken).ConfigureAwait(false);
        string hostId = Guid.NewGuid().ToString().ToUpperInvariant();

        PairRecord candidate = PairRecord.GenerateForDevice(devicePubKey, hostId, systemBuid, wifiMac);
        PlistDictionary pairOptions = new()
        {
            ["ExtendedPairingErrors"] = true,
        };

        DateTimeOffset deadline = DateTimeOffset.UtcNow + (trustPromptTimeout ?? TimeSpan.FromSeconds(60));
        while (true)
        {
            var req = new PlistDictionary
            {
                ["Request"] = "Pair",
                ["PairRecord"] = candidate.ToPlist(includePrivateKeys: false),
                ["PairingOptions"] = pairOptions,
            };

            PlistDictionary reply;
            await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                reply = await SendAndReceiveRawLockedAsync(req, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ioLock.Release();
            }

            string? error = reply.GetString("Error");
            if (string.IsNullOrEmpty(error))
            {
                byte[]? escrowBag = reply.GetData("EscrowBag");
                PairRecord finalRecord = escrowBag != null
                    ? new PairRecord
                    {
                        HostCertificate = candidate.HostCertificate,
                        HostPrivateKey = candidate.HostPrivateKey,
                        RootCertificate = candidate.RootCertificate,
                        RootPrivateKey = candidate.RootPrivateKey,
                        DeviceCertificate = candidate.DeviceCertificate,
                        HostId = candidate.HostId,
                        SystemBuid = candidate.SystemBuid,
                        WiFiMacAddress = candidate.WiFiMacAddress,
                        EscrowBag = escrowBag,
                    }
                    : candidate;

                if (saveToUsbmuxd)
                {
                    await usbmuxd.SavePairRecordAsync(device.SerialNumber, finalRecord, device.DeviceId, cancellationToken).ConfigureAwait(false);
                }

                PairRecord = finalRecord;
                return finalRecord;
            }

            if (string.Equals(error, "PairingDialogResponsePending", StringComparison.Ordinal) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new LockdownException($"Pairing failed: {error}", error);
        }
    }

    public async Task StartSessionAsync(PairRecord pairRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairRecord);

        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var req = new PlistDictionary
            {
                ["Request"] = "StartSession",
                ["HostID"] = pairRecord.HostId,
                ["SystemBUID"] = pairRecord.SystemBuid,
            };

            PlistDictionary reply = await RequestLockedAsync(req, cancellationToken).ConfigureAwait(false);
            SessionId = reply.GetString("SessionID");
            PairRecord = pairRecord;

            bool enableSsl = reply.GetBoolean("EnableSessionSSL") ?? false;
            if (enableSsl)
            {
                await UpgradeToSslLockedAsync(pairRecord, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task StopSessionAsync(CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrEmpty(SessionId))
            {
                return;
            }

            string sid = SessionId;
            await RequestLockedAsync(new PlistDictionary
            {
                ["Request"] = "StopSession",
                ["SessionID"] = sid,
            }, cancellationToken).ConfigureAwait(false);

            SessionId = null;
            await DowngradeSslLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task<LockdownServiceDescriptor> StartServiceAsync(
        string serviceName,
        bool withEscrowBag = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var req = new PlistDictionary
        {
            ["Request"] = "StartService",
            ["Service"] = serviceName,
        };

        if (withEscrowBag && PairRecord?.EscrowBag != null)
        {
            req["EscrowBag"] = PairRecord.EscrowBag;
        }

        PlistDictionary reply = await RequestAsync(req, cancellationToken).ConfigureAwait(false);
        long? portVal = reply.GetInt64("Port");
        if (portVal == null || portVal <= 0 || portVal > ushort.MaxValue)
        {
            throw new LockdownException($"StartService('{serviceName}') returned an invalid port.");
        }

        bool enableSsl = reply.GetBoolean("EnableServiceSSL") ?? false;
        string actualService = reply.GetString("Service") ?? serviceName;
        return new LockdownServiceDescriptor(actualService, (ushort)portVal.Value, enableSsl);
    }

    /// <summary>
    /// Starts a service via <c>lockdownd</c>, opens a <c>usbmuxd</c> connection to its port,
    /// and automatically upgrades to TLS if required by the service descriptor.
    /// </summary>
    public async Task<Stream> ConnectToServiceAsync(
        string serviceName,
        bool withEscrowBag = false,
        CancellationToken cancellationToken = default)
    {
        LockdownServiceDescriptor descriptor = await StartServiceAsync(serviceName, withEscrowBag, cancellationToken).ConfigureAwait(false);
        Stream serviceStream = await usbmuxd.ConnectAsync(device.DeviceId, descriptor.Port, cancellationToken).ConfigureAwait(false);

        if (descriptor.EnableServiceSsl)
        {
            if (PairRecord == null)
            {
                await serviceStream.DisposeAsync().ConfigureAwait(false);
                throw new LockdownException($"Service '{serviceName}' requires SSL, but no PairRecord is loaded.");
            }

            try
            {
                return await WrapStreamWithDeviceSslAsync(serviceStream, PairRecord, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await serviceStream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        return serviceStream;
    }

    private async Task UpgradeToSslLockedAsync(PairRecord pairRecord, CancellationToken cancellationToken)
    {
        sessionCert?.Dispose();
        sessionCert = pairRecord.CreateHostSslCertificate();

        var ssl = new SslStream(
            baseStream,
            leaveInnerStreamOpen: true,
            (_, cert, _, _) => pairRecord.VerifyRemoteDeviceCertificate(cert));

        try
        {
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = device.SerialNumber,
                ClientCertificates = new X509CertificateCollection { sessionCert },
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            transport = ssl;
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            transportFaulted = true;
            throw;
        }
    }

    private async Task DowngradeSslLockedAsync()
    {
        if (transport is SslStream ssl)
        {
            transport = baseStream;
            try
            {
                await ssl.ShutdownAsync().ConfigureAwait(false);
                // Drain the peer's TLS close_notify alert so the underlying stream is cleanly back at the plaintext boundary
                using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                byte[] drainBuf = new byte[64];
                while (await ssl.ReadAsync(drainBuf, drainCts.Token).ConfigureAwait(false) > 0)
                {
                }
            }
            catch
            {
                // Ignore timeout/peer close race during TLS shutdown.
            }

            await ssl.DisposeAsync().ConfigureAwait(false);
        }

        sessionCert?.Dispose();
        sessionCert = null;
    }

    internal static async Task<SslStream> WrapStreamWithDeviceSslAsync(Stream inner, PairRecord pairRecord, CancellationToken cancellationToken)
    {
        using var hostCert = pairRecord.CreateHostSslCertificate();
        var ssl = new SslStream(
            inner,
            leaveInnerStreamOpen: false,
            (_, cert, _, _) => pairRecord.VerifyRemoteDeviceCertificate(cert));

        try
        {
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ClientCertificates = new X509CertificateCollection { hostCert },
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            return ssl;
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<PlistDictionary> RequestAsync(PlistDictionary request, CancellationToken cancellationToken)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RequestLockedAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private async Task<PlistDictionary> RequestLockedAsync(PlistDictionary request, CancellationToken cancellationToken)
    {
        PlistDictionary reply = await SendAndReceiveRawLockedAsync(request, cancellationToken).ConfigureAwait(false);
        string? error = reply.GetString("Error");
        if (!string.IsNullOrEmpty(error))
        {
            throw new LockdownException($"lockdownd request '{request.GetString("Request")}' failed: {error}", error);
        }

        return reply;
    }

    private async Task<PlistDictionary> SendAndReceiveRawLockedAsync(PlistDictionary request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (transportFaulted)
        {
            throw new LockdownException("Lockdown transport is faulted after an interrupted or cancelled frame transfer.");
        }

        request["Label"] = label;
        byte[] payload = PlistSerializer.WriteXml(request);

        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(4));

        try
        {
            await transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await transport.FlushAsync(cancellationToken).ConfigureAwait(false);

            byte[] lengthPrefix = new byte[4];
            await transport.ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            uint replyLen = BinaryPrimitives.ReadUInt32BigEndian(lengthPrefix);
            if (replyLen == 0 || replyLen > 32 * 1024 * 1024)
            {
                throw new LockdownException($"Invalid lockdownd response length: {replyLen}.");
            }

            byte[] replyBytes = new byte[replyLen];
            await transport.ReadExactlyAsync(replyBytes, cancellationToken).ConfigureAwait(false);
            return PlistSerializer.Parse(replyBytes).AsDictionary();
        }
        catch
        {
            transportFaulted = true;
            try
            {
                if (!ReferenceEquals(transport, baseStream))
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

                await baseStream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore cleanup errors on faulted transport.
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (!transportFaulted && !string.IsNullOrEmpty(SessionId))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await StopSessionAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore shutdown errors on already closed sockets.
        }

        disposed = true;
        if (!ReferenceEquals(transport, baseStream))
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        await baseStream.DisposeAsync().ConfigureAwait(false);
        sessionCert?.Dispose();
        ioLock.Dispose();
    }
}
