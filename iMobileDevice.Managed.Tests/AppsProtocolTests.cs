namespace iMobileDevice.Managed.Tests;

using System.Buffers.Binary;
using System.Text;
using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Apps;
using iMobileDevice.Managed.Plist;
using Xunit;

public class AppsProtocolTests
{
    private const string BundleA = "com.example.alpha";
    private const string BundleB = "com.example.beta";

    [Fact]
    public async Task ListLooksUpApplicationsWithTypeAndPlaceholderOptions()
    {
        byte[] replies = Frames(new PlistDictionary
        {
            ["LookupResult"] = new PlistDictionary
            {
                [BundleB] = App(BundleB, "User"),
                [BundleA] = App(BundleA, "System"),
            },
            ["Status"] = "Complete",
        });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        IReadOnlyList<AppInfo> apps = await client.ListAsync(AppListType.Hidden, showPlaceholders: true);

        Assert.Equal(new[] { BundleA, BundleB }, apps.Select(static app => app.BundleIdentifier));
        Assert.Equal("System", apps[0].ApplicationType);
        Assert.Equal("User", apps[1].ApplicationType);
        (PlistDictionary request, _) = ParseFirstFrame(wire.BytesWritten);
        Assert.Equal("Lookup", request.GetString("Command"));
        PlistDictionary options = request.GetDictionary("ClientOptions")!;
        Assert.Equal("Hidden", options.GetString("ApplicationType"));
        Assert.True(options.GetBoolean("ShowPlaceholders"));
        Assert.False(options.ContainsKey("ReturnAttributes"));
        Assert.False(wire.Closed);
    }

    [Fact]
    public async Task ListWithoutPlaceholdersLooksUpExactlyLikeQuerySoBothAgreeOnWhatExists()
    {
        // The device omits offloaded applications unless ShowPlaceholders is set, so a list that
        // used another command would report apps that QueryAsync calls unknown.
        byte[] replies = Frames(new PlistDictionary
        {
            ["LookupResult"] = new PlistDictionary { [BundleA] = App(BundleA, "User") },
            ["Status"] = "Complete",
        });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        IReadOnlyList<AppInfo> apps = await client.ListAsync();

        Assert.Equal(new[] { BundleA }, apps.Select(static app => app.BundleIdentifier));
        (PlistDictionary request, _) = ParseFirstFrame(wire.BytesWritten);
        Assert.Equal("Lookup", request.GetString("Command"));
        PlistDictionary options = request.GetDictionary("ClientOptions")!;
        Assert.Equal("Any", options.GetString("ApplicationType"));
        Assert.False(options.ContainsKey("ShowPlaceholders"));
    }

    [Fact]
    public async Task ListMergesBatchesUntilTheDeviceReportsComplete()
    {
        // The device chooses the batch marker; every status other than Complete keeps the loop reading.
        byte[] replies = Frames(
            new PlistDictionary { ["LookupResult"] = new PlistDictionary { [BundleB] = App(BundleB, "User") }, ["Status"] = "InProgress" },
            new PlistDictionary { ["LookupResult"] = new PlistDictionary { [BundleA] = App(BundleA, "User") }, ["Status"] = "Complete" });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        IReadOnlyList<AppInfo> apps = await client.ListAsync();

        Assert.Equal(new[] { BundleA, BundleB }, apps.Select(static app => app.BundleIdentifier));
    }

    [Fact]
    public async Task ListDisconnectBetweenBatchesIsATransportFailureRatherThanAPartialList()
    {
        byte[] replies = Frames(new PlistDictionary
        {
            ["LookupResult"] = new PlistDictionary { [BundleA] = App(BundleA, "User") },
            ["Status"] = "InProgress",
        });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        await Assert.ThrowsAsync<EndOfStreamException>(() => client.ListAsync());
    }

    [Fact]
    public async Task ListAddsDiskUsageFromASecondLookupWhenSizesAreRequested()
    {
        byte[] replies = Frames(
            new PlistDictionary
            {
                ["LookupResult"] = new PlistDictionary { [BundleA] = App(BundleA, "User") },
                ["Status"] = "Complete",
            },
            new PlistDictionary
            {
                ["LookupResult"] = new PlistDictionary
                {
                    [BundleA] = new PlistDictionary { ["StaticDiskUsage"] = 4096, ["DynamicDiskUsage"] = 512 },
                },
                ["Status"] = "Complete",
            });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        IReadOnlyList<AppInfo> apps = await client.ListAsync(calculateSizes: true);

        AppInfo app = Assert.Single(apps);
        Assert.Equal(4096, app.StaticDiskUsage);
        Assert.Equal(512, app.DynamicDiskUsage);
        List<PlistNode> requests = ParseFrames(wire.BytesWritten);
        Assert.Equal("Lookup", requests[0].AsDictionary().GetString("Command"));
        PlistDictionary second = requests[1].AsDictionary();
        Assert.Equal("Lookup", second.GetString("Command"));
        PlistDictionary options = second.GetDictionary("ClientOptions")!;
        Assert.Equal("Any", options.GetString("ApplicationType"));
        Assert.Equal(
            new[] { "CFBundleIdentifier", "StaticDiskUsage", "DynamicDiskUsage" },
            options.GetArray("ReturnAttributes")!.Select(static attribute => attribute.AsString()));
        Assert.False(options.ContainsKey("BundleIDs"));
    }

    [Fact]
    public async Task QuerySendsEveryBundleIdentifierOnceAndReturnsOnlyInstalledApps()
    {
        byte[] replies = Frames(new PlistDictionary
        {
            ["LookupResult"] = new PlistDictionary { [BundleB] = new PlistDictionary { ["CFBundleIdentifier"] = BundleB } },
            ["Status"] = "Complete",
        });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        IReadOnlyList<AppInfo> apps = await client.QueryAsync([BundleA, BundleB, BundleB]);

        Assert.Equal(new[] { BundleB }, apps.Select(static app => app.BundleIdentifier));
        (PlistDictionary request, _) = ParseFirstFrame(wire.BytesWritten);
        Assert.Equal("Lookup", request.GetString("Command"));
        PlistDictionary options = request.GetDictionary("ClientOptions")!;
        Assert.Equal(new[] { BundleA, BundleB }, options.GetArray("BundleIDs")!.Select(static id => id.AsString()));
        Assert.Equal("Any", options.GetString("ApplicationType"));
        Assert.False(options.ContainsKey("ReturnAttributes"));
    }

    [Fact]
    public async Task QueryWithoutMatchesReturnsAnEmptyResultInsteadOfFailing()
    {
        byte[] replies = Frames(new PlistDictionary { ["LookupResult"] = new PlistDictionary(), ["Status"] = "Complete" });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        Assert.Empty(await client.QueryAsync(["com.example.missing"]));
        Assert.False(wire.Closed);
    }

    [Fact]
    public async Task QueryWithNoBundleIdentifiersIsRejectedBeforeAnyRequest()
    {
        using var wire = new DuplexStream(Array.Empty<byte>());
        await using var client = new InstallationProxyClient(wire);

        await Assert.ThrowsAsync<ArgumentException>(() => client.QueryAsync([]));
        Assert.Empty(wire.BytesWritten);
    }

    [Fact]
    public async Task DeviceErrorSurfacesAsAppsExceptionAndRetiresTheClient()
    {
        byte[] replies = Frames(new PlistDictionary
        {
            ["Error"] = "BadCommand",
            ["ErrorDescription"] = "Unknown command",
        });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        var error = await Assert.ThrowsAsync<AppsException>(() => client.QueryAsync([BundleA]));
        Assert.Equal("BadCommand", error.DeviceError);
        Assert.Equal("Unknown command", error.DeviceErrorDescription);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.QueryAsync([BundleA]));
        Assert.True(wire.Closed);
    }

    [Fact]
    public async Task TruncatedReplyIsReportedAsATransportFailure()
    {
        byte[] frame = Frame(new PlistDictionary
        {
            ["LookupResult"] = new PlistDictionary { [BundleA] = App(BundleA, "User") },
            ["Status"] = "Complete",
        });

        using var wire = new DuplexStream(frame[..^12]);
        await using var client = new InstallationProxyClient(wire);

        await Assert.ThrowsAsync<EndOfStreamException>(() => client.ListAsync());
    }

    [Fact]
    public async Task ReplyWithoutAnyResultBeforeTheStreamEndsIsATransportFailure()
    {
        using var wire = new DuplexStream(Frames(new PlistDictionary()));
        await using var client = new InstallationProxyClient(wire);

        await Assert.ThrowsAsync<IOException>(() => client.ListAsync());
    }

    [Fact]
    public async Task LookupReplyWithoutResultIsAProtocolError()
    {
        byte[] replies = Frames(new PlistDictionary { ["Status"] = "Complete" });

        using var wire = new DuplexStream(replies);
        await using var client = new InstallationProxyClient(wire);

        await Assert.ThrowsAsync<AppsException>(() => client.QueryAsync([BundleA]));
    }

    [Fact]
    public async Task VendedDocumentsSessionListsTheContainerOverAfcAndReleasesTheStreamOnDispose()
    {
        byte[] replies = Frames(new PlistDictionary())
            .Concat(AfcPacket(1, AfcOperation.Data, "Reports\0Notes\0"u8))
            .ToArray();

        using var wire = new DuplexStream(replies);
        AfcClient afc = await AppContainer.OpenAsync(wire, BundleA, AppContainerScope.Documents);
        Assert.False(wire.Closed);

        IReadOnlyList<string> documents = await afc.ReadDirectoryAsync("/Documents");
        Assert.Equal(new[] { "Reports", "Notes" }, documents);

        (PlistDictionary vend, int consumed) = ParseFirstFrame(wire.BytesWritten);
        Assert.Equal("VendDocuments", vend.GetString("Command"));
        Assert.Equal(BundleA, vend.GetString("Identifier"));
        Assert.True(wire.BytesWritten.AsSpan(consumed, 8).SequenceEqual("CFA6LPAA"u8));
        Assert.True(wire.BytesWritten.AsSpan(consumed + 40).SequenceEqual("/Documents\0"u8));

        await afc.DisposeAsync();
        Assert.True(wire.Closed);
    }

    [Fact]
    public async Task VendedContainerScopeSendsVendContainer()
    {
        using var wire = new DuplexStream(Frames(new PlistDictionary()));

        await using (AfcClient afc = await AppContainer.OpenAsync(wire, BundleB, AppContainerScope.Container))
        {
            (PlistDictionary vend, _) = ParseFirstFrame(wire.BytesWritten);
            Assert.Equal("VendContainer", vend.GetString("Command"));
            Assert.Equal(BundleB, vend.GetString("Identifier"));
        }

        Assert.True(wire.Closed);
    }

    [Fact]
    public async Task MissingApplicationIsReportedSeparatelyFromOtherRefusalsAndReleasesTheStream()
    {
        using var missing = new DuplexStream(Frames(new PlistDictionary { ["Error"] = "ApplicationLookupFailed" }));
        var notInstalled = await Assert.ThrowsAsync<AppNotInstalledException>(
            () => AppContainer.OpenAsync(missing, "com.example.absent", AppContainerScope.Documents));
        Assert.Equal("ApplicationLookupFailed", notInstalled.DeviceError);
        Assert.True(missing.Closed);

        using var refused = new DuplexStream(Frames(new PlistDictionary
        {
            ["Error"] = "InternalError",
            ["ErrorDescription"] = "Vend failed",
        }));
        var error = await Assert.ThrowsAsync<AppsException>(
            () => AppContainer.OpenAsync(refused, BundleA, AppContainerScope.Documents));
        Assert.IsNotAssignableFrom<AppNotInstalledException>(error);
        Assert.Equal("InternalError", error.DeviceError);
        Assert.Equal("Vend failed", error.DeviceErrorDescription);
        Assert.True(refused.Closed);
    }

    [Fact]
    public async Task InvalidBundleIdentifierReleasesTheStreamInsteadOfLeakingTheServiceConnection()
    {
        using var wire = new DuplexStream(Array.Empty<byte>());

        await Assert.ThrowsAsync<ArgumentException>(
            () => AppContainer.OpenAsync(wire, string.Empty, AppContainerScope.Documents));
        Assert.True(wire.Closed);
        Assert.Empty(wire.BytesWritten);
    }

    [Fact]
    public async Task CancelledVendReleasesTheStream()
    {
        using var wire = new DuplexStream(Frames(new PlistDictionary()));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AppContainer.OpenAsync(wire, BundleA, AppContainerScope.Documents, cancel.Token));
        Assert.True(wire.Closed);
    }

    private static PlistDictionary App(string bundleIdentifier, string applicationType) => new()
    {
        ["CFBundleIdentifier"] = bundleIdentifier,
        ["ApplicationType"] = applicationType,
    };

    private static byte[] Frames(params PlistDictionary[] replies) => replies.SelectMany(Frame).ToArray();

    private static byte[] Frame(PlistNode value)
    {
        byte[] body = PlistSerializer.WriteBinary(value);
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }

    private static byte[] AfcPacket(ulong packetNumber, AfcOperation operation, ReadOnlySpan<byte> payload)
    {
        ulong thisLength = (ulong)(40 + payload.Length);
        byte[] packet = new byte[thisLength];
        "CFA6LPAA"u8.CopyTo(packet.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(8, 8), thisLength);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(16, 8), thisLength);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(24, 8), packetNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(32, 8), (ulong)operation);
        payload.CopyTo(packet.AsSpan(40));
        return packet;
    }

    private static (PlistDictionary Request, int Consumed) ParseFirstFrame(byte[] written)
    {
        uint length = BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(0, 4));
        return (PlistSerializer.Parse(written.AsSpan(4, (int)length).ToArray()).AsDictionary(), 4 + (int)length);
    }

    private static List<PlistNode> ParseFrames(byte[] written)
    {
        var requests = new List<PlistNode>();
        int offset = 0;
        while (offset < written.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(offset, 4));
            requests.Add(PlistSerializer.Parse(written.AsSpan(offset + 4, (int)length).ToArray()));
            offset += 4 + (int)length;
        }

        return requests;
    }

    private sealed class DuplexStream(byte[] replies) : Stream
    {
        private readonly MemoryStream readStream = new(replies, writable: false);
        private readonly MemoryStream writeStream = new();

        public byte[] BytesWritten => writeStream.ToArray();
        public bool Closed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => readStream.Length;
        public override long Position { get => readStream.Position; set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => readStream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            readStream.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => writeStream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            writeStream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }
}
