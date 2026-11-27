namespace iMobileDevice.Managed.Dvt;

using System.Runtime.CompilerServices;

/// <summary>The app's current PID and the measurements reported for it in one sysmontap batch.</summary>
/// <param name="ProcessId">Null when Instruments reports that the bundle is not running.</param>
/// <param name="Measurement">Null when this batch has no measurement for the current process instance.</param>
public sealed record ApplicationProcessObservation(int? ProcessId, ProcessMeasurement? Measurement)
{
    internal static ApplicationProcessObservation FromBatch(int? previousPid, int? currentPid,
        IReadOnlyList<ProcessMeasurement> batch)
    {
        // At a launch or relaunch boundary, the batch can predate the new process.
        var measurement = currentPid is int pid && pid == previousPid
            ? batch.FirstOrDefault(p => p.ProcessId == pid)
            : null;
        return new ApplicationProcessObservation(currentPid, measurement);
    }
}

public sealed partial class DvtClient
{
    private const string ProcessControlService = "com.apple.instruments.server.services.processcontrol";

    /// <summary>Resolves an exact application bundle identifier to its running PID, or null if absent.</summary>
    public async Task<int?> GetApplicationProcessIdAsync(string bundleIdentifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleIdentifier);
        uint channel = await OpenChannelAsync(ProcessControlService, cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadApplicationProcessIdAsync(channel, bundleIdentifier, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CloseChannelAsync(channel).ConfigureAwait(false);
        }
    }

    /// <summary>Observes the main process of a single application by bundle identifier across launches and exits.</summary>
    /// <remarks>Yields one observation per delivered process batch. CPU and memory have the same units and
    /// warmup as <see cref="WatchProcessesAsync"/>. After a PID change, measurements resume in a subsequent batch.</remarks>
    public async IAsyncEnumerable<ApplicationProcessObservation> WatchApplicationAsync(string bundleIdentifier,
        int intervalMilliseconds = 1000, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleIdentifier);
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalMilliseconds, 1);
        uint channel = await OpenChannelAsync(ProcessControlService, cancellationToken).ConfigureAwait(false);
        try
        {
            int? previousPid = await ReadApplicationProcessIdAsync(channel, bundleIdentifier, cancellationToken).ConfigureAwait(false);
            await foreach (var batch in WatchProcessesAsync(intervalMilliseconds, cancellationToken).ConfigureAwait(false))
            {
                int? currentPid = await ReadApplicationProcessIdAsync(channel, bundleIdentifier, cancellationToken).ConfigureAwait(false);
                var observation = ApplicationProcessObservation.FromBatch(previousPid, currentPid, batch);
                previousPid = currentPid;
                cancellationToken.ThrowIfCancellationRequested();
                yield return observation;
            }
        }
        finally
        {
            await CloseChannelAsync(channel).ConfigureAwait(false);
        }
    }

    private async Task<int?> ReadApplicationProcessIdAsync(uint channel, string bundleIdentifier, CancellationToken cancellationToken)
    {
        var reply = await dtx.InvokeAsync(channel, "processIdentifierForBundleIdentifier:",
            new[] { DtxAuxArg.Archived(bundleIdentifier) }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return reply.Payload switch
        {
            0L or 0UL => null,
            long pid when pid is > 0 and <= int.MaxValue => (int)pid,
            ulong pid when pid is > 0 and <= int.MaxValue => (int)pid,
            _ => throw new InvalidDataException("Instruments returned an invalid application PID."),
        };
    }
}
