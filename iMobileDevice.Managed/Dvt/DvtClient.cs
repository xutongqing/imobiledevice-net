namespace iMobileDevice.Managed.Dvt;

using iMobileDevice.Managed.RemoteXpc;

/// <summary>
/// Pure managed iOS 17+ / 18 DVT (Developer Tools) client over <c>com.apple.instruments.dtservicehub</c>,
/// supporting live PNG screen capture (<c>takeScreenshot</c>) and extensible DTX service channels.
/// </summary>
public sealed class DvtClient : IAsyncDisposable
{
    public const string DtServiceHubName = "com.apple.instruments.dtservicehub";
    public const string ScreenshotServiceIdentifier = "com.apple.instruments.server.services.screenshot";
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly DtxConnection dtx;
    private int nextChannelCode;
    private uint? screenshotChannel;

    private DvtClient(DtxConnection dtx)
    {
        this.dtx = dtx;
    }

    public static async Task<DvtClient> ConnectAsync(RemoteServiceDiscoveryClient rsd, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rsd);

        Stream stream = await rsd.ConnectToServiceAsync(DtServiceHubName, cancellationToken).ConfigureAwait(false);
        var dtx = new DtxConnection(stream);
        var client = new DvtClient(dtx);
        try
        {
            await client.PublishCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task PublishCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var capabilities = new Dictionary<string, object?>
        {
            // Advertise 0 until DTX block decompression is implemented
            ["com.apple.private.DTXBlockCompression"] = 0,
            ["com.apple.private.DTXConnection"] = 1,
        };

        await dtx.InvokeAsync(
            channelCode: 0,
            selector: "_notifyOfPublishedCapabilities:",
            args: new[] { DtxAuxArg.Archived(capabilities) },
            expectsReply: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<uint> OpenChannelAsync(string serviceIdentifier, CancellationToken cancellationToken = default)
    {
        uint code = (uint)Interlocked.Increment(ref nextChannelCode);
        await dtx.InvokeAsync(
            channelCode: 0,
            selector: "_requestChannelWithCode:identifier:",
            args: new[]
            {
                DtxAuxArg.Int32((int)code),
                DtxAuxArg.Archived(serviceIdentifier),
            },
            expectsReply: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return code;
    }

    /// <summary>
    /// Captures a live PNG screenshot from the connected iOS 17+ / 18 device.
    /// </summary>
    public async Task<byte[]> TakeScreenshotAsync(CancellationToken cancellationToken = default)
    {
        uint channel = screenshotChannel ??= await OpenChannelAsync(ScreenshotServiceIdentifier, cancellationToken).ConfigureAwait(false);
        DtxMessage reply = await dtx.InvokeAsync(
            channelCode: channel,
            selector: "takeScreenshot",
            args: null,
            expectsReply: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (reply.Payload is byte[] png && png.Length >= PngMagic.Length && png.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic))
        {
            return png;
        }

        throw new InvalidDataException($"DVT takeScreenshot did not return a valid PNG payload (got {reply.Payload?.GetType().Name ?? "null"}).");
    }

    public ValueTask DisposeAsync() => dtx.DisposeAsync();
}
