namespace iMobileDevice.Managed.Dvt;

using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>A process reported by Instruments deviceinfo.</summary>
public sealed record DeviceProcess(int ProcessId, string Name);

/// <summary>CPU usage in percent of one core and physical footprint in bytes. Missing measurements are null.</summary>
public sealed record ProcessMeasurement(int ProcessId, string? Name, double? CpuPercent, long? PhysicalFootprintBytes);

public sealed partial class DvtClient
{
    /// <summary>Lists the processes currently reported by the selected device.</summary>
    public async Task<IReadOnlyList<DeviceProcess>> ListProcessesAsync(CancellationToken cancellationToken = default)
    {
        uint channel = await OpenChannelAsync("com.apple.instruments.server.services.deviceinfo", cancellationToken).ConfigureAwait(false);
        try
        {
            var reply = await dtx.InvokeAsync(channel, "runningProcesses", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (reply.Payload is not List<object?> rows)
            {
                throw new InvalidDataException("Instruments returned an invalid runningProcesses list.");
            }

            var processes = new List<DeviceProcess>(rows.Count);
            foreach (var row in rows)
            {
                if (row is not Dictionary<string, object?> fields || fields.GetValueOrDefault("pid") is not long pid ||
                    fields.GetValueOrDefault("name") is not string name)
                {
                    throw new InvalidDataException("Instruments returned an invalid process entry.");
                }
                processes.Add(new DeviceProcess(checked((int)pid), name));
            }
            return processes;
        }
        finally
        {
            await CloseChannelAsync(channel).ConfigureAwait(false);
        }
    }

    /// <summary>Streams batches of process measurements after the initial CPU warmup.</summary>
    /// <remarks>Each batch includes the PIDs reported for that interval. Slow readers can miss batches.
    /// Cancel or dispose the enumerator to stop sampling. Transport failures end the stream with an exception.</remarks>
    public async IAsyncEnumerable<IReadOnlyList<ProcessMeasurement>> WatchProcessesAsync(
        int intervalMilliseconds = 1000, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalMilliseconds, 1);
        uint channel = await OpenChannelAsync("com.apple.instruments.server.services.sysmontap", cancellationToken).ConfigureAwait(false);
        var inbox = dtx.Subscribe(channel);
        try
        {
            var config = new Dictionary<string, object?>
            {
                ["ur"] = intervalMilliseconds,
                ["sampleInterval"] = (long)intervalMilliseconds * 1_000_000,
                ["bm"] = 0,
                ["cpuUsage"] = true,
                ["physFootprint"] = true,
                ["procAttrs"] = new object[] { "name", "cpuUsage", "physFootprint", "pid" },
                ["sysAttrs"] = new object[] { "vmUsedCount" }, // Request one system counter alongside the process rows.
            };
            await dtx.InvokeAsync(channel, "setConfig:", new[] { DtxAuxArg.Archived(config) }, false, cancellationToken).ConfigureAwait(false);
            await dtx.InvokeAsync(channel, "start", expectsReply: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            bool warmup = true;
            await foreach (var message in inbox.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var snapshot in DecodeProcessSnapshots(message.Payload))
                {
                    // The first sample has no previous CPU counters to compare against.
                    if (warmup)
                    {
                        warmup = false;
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return snapshot;
                }
            }
        }
        finally
        {
            dtx.Unsubscribe(channel);
            await CloseChannelAsync(channel, stopSampling: true).ConfigureAwait(false);
        }
    }

    private async Task CloseChannelAsync(uint channel, bool stopSampling = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            if (stopSampling)
            {
                await dtx.InvokeAsync(channel, "stop", expectsReply: false, cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            await dtx.InvokeAsync(0, "_channelCanceled:", new[] { DtxAuxArg.Int32((int)channel) }, false, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // A disconnected device cannot acknowledge further channel cleanup.
        }
    }

    internal static IEnumerable<IReadOnlyList<ProcessMeasurement>> DecodeProcessSnapshots(object? payload)
    {
        if (payload is byte[] bytes)
        {
            payload = NsKeyedArchiver.Unarchive(bytes);
        }
        if (payload is Dictionary<string, object?> envelope && envelope.TryGetValue("DTTapMessagePlist", out var plist))
        {
            payload = plist;
        }

        if (payload is Dictionary<string, object?> status && status.GetValueOrDefault("status") is long code && (code & 0x80000000) != 0)
        {
            throw new InvalidDataException($"Instruments sysmontap error 0x{code:X8}: {status.GetValueOrDefault("notice")}");
        }

        if (payload is not List<object?> rows)
        {
            yield break;
        }

        foreach (var row in rows)
        {
            if (row is not Dictionary<string, object?> fields ||
                fields.GetValueOrDefault("Processes") is not Dictionary<string, object?> processes)
            {
                continue;
            }
            var sample = new List<ProcessMeasurement>(processes.Count);
            foreach (var (key, value) in processes)
            {
                if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || value is not List<object?> attrs || attrs.Count != 4)
                {
                    throw new InvalidDataException("Instruments returned an unexpected sysmontap process record.");
                }
                double? cpu = attrs[1] is null ? null : Convert.ToDouble(attrs[1], CultureInfo.InvariantCulture);
                long? memory = attrs[2] is null ? null : Convert.ToInt64(attrs[2], CultureInfo.InvariantCulture);
                sample.Add(new ProcessMeasurement(pid, attrs[0] as string, cpu, memory));
            }
            yield return sample;
        }
    }
}
