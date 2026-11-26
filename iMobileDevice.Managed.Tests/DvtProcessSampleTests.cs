namespace iMobileDevice.Managed.Tests;

using iMobileDevice.Managed.Dvt;
using iMobileDevice.Managed.Plist;
using Xunit;

public class DvtProcessSampleTests
{
    [Fact]
    public void DecodesOnlyProcessRowsAndPreservesMissingMeasurements()
    {
        var rows = new object[]
        {
            new Dictionary<string, object?> { ["CPUCount"] = 6L },
            new Dictionary<string, object?> { ["Processes"] = new Dictionary<string, object?>
            {
                ["21"] = new object?[] { "one", 150.5, 123456L, 21L },
                ["42"] = new object?[] { "two", null, null, 42L },
            } },
        };
        var payload = NsKeyedArchiver.Archive(rows);
        var batch = Assert.Single(DvtClient.DecodeProcessSnapshots(payload));
        Assert.Contains(new ProcessMeasurement(21, "one", 150.5, 123456), batch);
        Assert.Contains(new ProcessMeasurement(42, "two", null, null), batch);
    }

    [Fact]
    public void ReportsSysmontapConfigurationErrorsToTheReader()
    {
        var status = new Dictionary<string, object?> { ["status"] = 0x80000000L, ["notice"] = "Unavailable attribute" };
        var envelope = new Dictionary<string, object?> { ["DTTapMessagePlist"] = status };
        var error = Assert.Throws<InvalidDataException>(() => DvtClient.DecodeProcessSnapshots(envelope).ToList());
        Assert.Contains("Unavailable attribute", error.Message);
    }

    [Fact]
    public void ResolvesArchivedNSNullUsedByInstruments()
    {
        var archive = new PlistDictionary
        {
            ["$top"] = new PlistDictionary { ["root"] = new PlistUid(1) },
            ["$objects"] = new PlistArray
            {
                "$null",
                new PlistDictionary { ["$class"] = new PlistUid(2) },
                new PlistDictionary { ["$classname"] = "NSNull" },
            },
        };
        Assert.Null(NsKeyedArchiver.Unarchive(PlistSerializer.WriteBinary(archive)));
    }
}
