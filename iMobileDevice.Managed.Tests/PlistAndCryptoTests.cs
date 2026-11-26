namespace iMobileDevice.Managed.Tests;

using System.Security.Cryptography;
using System.Text;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;
using Xunit;

public class PlistAndCryptoTests
{
    [Theory]
    [InlineData(PlistFormat.Xml)]
    [InlineData(PlistFormat.Binary)]
    public void PlistRoundTripSupportsAllTypes(PlistFormat format)
    {
        var date = new DateTimeOffset(2026, 11, 26, 15, 30, 0, TimeSpan.Zero);
        var root = new PlistDictionary
        {
            ["ascii"] = "hello-world",
            ["unicode"] = "小雨的小号iPhone 🚀",
            ["boolTrue"] = true,
            ["boolFalse"] = false,
            ["negInt"] = -42L,
            ["posInt"] = 1234567890123L,
            ["ulongInt"] = ulong.MaxValue - 5,
            ["real"] = 3.1415926535,
            ["bytes"] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF },
            ["date"] = date,
            ["array"] = new PlistArray { "a", 1, true, new byte[] { 1, 2, 3 } },
            ["nested"] = new PlistDictionary
            {
                ["innerKey"] = "innerVal",
                ["emptyArr"] = new PlistArray(),
                ["emptyDict"] = new PlistDictionary(),
            },
        };

        byte[] serialized = PlistSerializer.Serialize(root, format);
        Assert.Equal(format == PlistFormat.Binary, PlistSerializer.IsBinary(serialized));

        PlistDictionary parsed = PlistSerializer.Parse(serialized).AsDictionary();
        Assert.Equal("hello-world", parsed.GetString("ascii"));
        Assert.Equal("小雨的小号iPhone 🚀", parsed.GetString("unicode"));
        Assert.Equal(true, parsed.GetBoolean("boolTrue"));
        Assert.Equal(false, parsed.GetBoolean("boolFalse"));
        Assert.Equal(-42L, parsed.GetInt64("negInt"));
        Assert.Equal(1234567890123L, parsed.GetInt64("posInt"));
        Assert.Equal(ulong.MaxValue - 5, parsed.GetUInt64("ulongInt"));
        Assert.Equal(3.1415926535, parsed["real"].AsReal(), precision: 9);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF }, parsed.GetData("bytes"));
        Assert.Equal(date, parsed["date"].AsDate());

        PlistArray arr = parsed.GetArray("array")!;
        Assert.Equal(4, arr.Count);
        Assert.Equal("a", arr[0].AsString());
        Assert.Equal(1L, arr[1].AsInt64());
        Assert.True(arr[2].AsBoolean());
        Assert.Equal(new byte[] { 1, 2, 3 }, arr[3].AsData());

        PlistDictionary nested = parsed.GetDictionary("nested")!;
        Assert.Equal("innerVal", nested.GetString("innerKey"));
        Assert.Empty(nested.GetArray("emptyArr")!);
        Assert.Empty(nested.GetDictionary("emptyDict")!);
    }

    [Fact]
    public void PairRecordCertificateGenerationAndSslCertExportWorks()
    {
        using var fakeDeviceRsa = RSA.Create(2048);
        byte[] fakeDevicePubPem = Encoding.ASCII.GetBytes(fakeDeviceRsa.ExportRSAPublicKeyPem() + "\n");

        PairRecord record = PairRecord.GenerateForDevice(fakeDevicePubPem, "TEST-HOST-ID", "TEST-BUID", "aa:bb:cc:dd:ee:ff");
        Assert.NotEmpty(record.RootCertificate);
        Assert.NotEmpty(record.HostCertificate);
        Assert.NotEmpty(record.DeviceCertificate);

        byte[] plistBytes = PlistSerializer.WriteXml(record.ToPlist());
        PairRecord roundTripped = PairRecord.FromPlist(PlistSerializer.Parse(plistBytes).AsDictionary());
        Assert.Equal("TEST-HOST-ID", roundTripped.HostId);
        Assert.Equal("TEST-BUID", roundTripped.SystemBuid);

        using var sslCert = roundTripped.CreateHostSslCertificate();
        Assert.True(sslCert.HasPrivateKey);
    }
}
