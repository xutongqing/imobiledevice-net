namespace iMobileDevice.Managed.Lockdown;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iMobileDevice.Managed.Plist;

/// <summary>
/// Represents a lockdown pairing record containing host/root/device PEM certificates and metadata.
/// </summary>
public sealed class PairRecord
{
    public required byte[] HostCertificate { get; init; }
    public required byte[] HostPrivateKey { get; init; }
    public required byte[] RootCertificate { get; init; }
    public required byte[] RootPrivateKey { get; init; }
    public required byte[] DeviceCertificate { get; init; }
    public required string HostId { get; init; }
    public required string SystemBuid { get; init; }
    public byte[]? EscrowBag { get; init; }
    public string? WiFiMacAddress { get; init; }

    public static PairRecord FromPlist(PlistDictionary dict)
    {
        ArgumentNullException.ThrowIfNull(dict);

        return new PairRecord
        {
            HostCertificate = dict.GetData("HostCertificate") ?? throw new FormatException("PairRecord missing HostCertificate."),
            HostPrivateKey = dict.GetData("HostPrivateKey") ?? throw new FormatException("PairRecord missing HostPrivateKey."),
            RootCertificate = dict.GetData("RootCertificate") ?? throw new FormatException("PairRecord missing RootCertificate."),
            RootPrivateKey = dict.GetData("RootPrivateKey") ?? throw new FormatException("PairRecord missing RootPrivateKey."),
            DeviceCertificate = dict.GetData("DeviceCertificate") ?? throw new FormatException("PairRecord missing DeviceCertificate."),
            HostId = dict.GetString("HostID") ?? throw new FormatException("PairRecord missing HostID."),
            SystemBuid = dict.GetString("SystemBUID") ?? throw new FormatException("PairRecord missing SystemBUID."),
            EscrowBag = dict.GetData("EscrowBag"),
            WiFiMacAddress = dict.GetString("WiFiMACAddress"),
        };
    }

    public PlistDictionary ToPlist(bool includePrivateKeys = true)
    {
        var dict = new PlistDictionary
        {
            ["DeviceCertificate"] = DeviceCertificate,
            ["HostCertificate"] = HostCertificate,
            ["HostID"] = HostId,
            ["RootCertificate"] = RootCertificate,
            ["SystemBUID"] = SystemBuid,
        };

        if (includePrivateKeys)
        {
            dict["HostPrivateKey"] = HostPrivateKey;
            dict["RootPrivateKey"] = RootPrivateKey;
        }

        if (EscrowBag != null && EscrowBag.Length > 0)
        {
            dict["EscrowBag"] = EscrowBag;
        }

        if (!string.IsNullOrEmpty(WiFiMacAddress))
        {
            dict["WiFiMACAddress"] = WiFiMacAddress;
        }

        return dict;
    }

    /// <summary>
    /// Loads the host client certificate + private key in a form compatible with <see cref="System.Net.Security.SslStream"/>
    /// across macOS, Linux, and Windows SChannel.
    /// </summary>
    public X509Certificate2 CreateHostSslCertificate()
    {
        string certPem = Encoding.ASCII.GetString(HostCertificate);
        string keyPem = Encoding.ASCII.GetString(HostPrivateKey);

        using var ephemeral = X509Certificate2.CreateFromPem(certPem, keyPem);
        byte[] pfx = ephemeral.Export(X509ContentType.Pkcs12);
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Verifies that the peer certificate presented during TLS handshake matches the paired device certificate
    /// (exact DER match or matching public key).
    /// </summary>
    public bool VerifyRemoteDeviceCertificate(X509Certificate? remoteCertificate)
    {
        if (remoteCertificate == null)
        {
            return false;
        }

        string expectedPem = Encoding.ASCII.GetString(DeviceCertificate);
        using var expected = X509Certificate2.CreateFromPem(expectedPem);

        byte[] actualRaw = remoteCertificate.GetRawCertData();
        if (actualRaw.AsSpan().SequenceEqual(expected.RawData))
        {
            return true;
        }

        byte[] actualPubKey = remoteCertificate.GetPublicKey();
        byte[] expectedPubKey = expected.GetPublicKey();
        return actualPubKey.Length > 0 && actualPubKey.AsSpan().SequenceEqual(expectedPubKey);
    }

    /// <summary>
    /// Generates a fresh set of Root, Host, and Device RSA certificates for pairing with an iOS device.
    /// </summary>
    public static PairRecord GenerateForDevice(byte[] devicePublicKeyPem, string hostId, string systemBuid, string? wifiMacAddress = null)
    {
        ArgumentNullException.ThrowIfNull(devicePublicKeyPem);

        using var rootKey = RSA.Create(2048);
        using var hostKey = RSA.Create(2048);
        using var devicePubKey = RSA.Create();
        devicePubKey.ImportFromPem(Encoding.ASCII.GetString(devicePublicKeyPem));

        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset notAfter = notBefore.AddYears(10);

        // 1. Self-signed Root CA
        var rootReq = new CertificateRequest(new X500DistinguishedName("CN=iMobileDevice.Managed Root"), rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        rootReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootReq.PublicKey, critical: false));
        using var rootCert = rootReq.CreateSelfSigned(notBefore, notAfter);

        // 2. Host certificate signed by Root CA
        var hostReq = new CertificateRequest(new X500DistinguishedName("CN=iMobileDevice.Managed Host"), hostKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        hostReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        hostReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        hostReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(hostReq.PublicKey, false));
        byte[] hostSerial = RandomNumberGenerator.GetBytes(8);
        hostSerial[0] &= 0x7F;
        using var hostCert = hostReq.Create(rootCert, notBefore, notAfter, hostSerial);

        // 3. Device certificate signed by Root CA using device's public key
        var devReq = new CertificateRequest(new X500DistinguishedName("CN=iOS Device"), devicePubKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        devReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        devReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        devReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(devReq.PublicKey, false));
        byte[] devSerial = RandomNumberGenerator.GetBytes(8);
        devSerial[0] &= 0x7F;
        using var devCert = devReq.Create(rootCert, notBefore, notAfter, devSerial);

        return new PairRecord
        {
            RootCertificate = Encoding.ASCII.GetBytes(rootCert.ExportCertificatePem() + "\n"),
            RootPrivateKey = Encoding.ASCII.GetBytes(rootKey.ExportPkcs8PrivateKeyPem() + "\n"),
            HostCertificate = Encoding.ASCII.GetBytes(hostCert.ExportCertificatePem() + "\n"),
            HostPrivateKey = Encoding.ASCII.GetBytes(hostKey.ExportPkcs8PrivateKeyPem() + "\n"),
            DeviceCertificate = Encoding.ASCII.GetBytes(devCert.ExportCertificatePem() + "\n"),
            HostId = hostId,
            SystemBuid = systemBuid,
            WiFiMacAddress = wifiMacAddress,
        };
    }
}
