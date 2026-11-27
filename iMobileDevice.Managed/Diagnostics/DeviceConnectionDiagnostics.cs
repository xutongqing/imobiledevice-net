namespace iMobileDevice.Managed.Diagnostics;

using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Xml;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Usbmuxd;

public enum DiscoveryStatus
{
    DevicesFound, NoDevices, NoMatch, DaemonUnavailable, TimedOut, Failed,
}

public enum DeviceConnectionStatus
{
    Connected, PairingRequired, PairingRejected, DeviceLocked, Disconnected,
    LockdownUnavailable, AuthenticationFailed, TimedOut, Failed,
}

public sealed record DeviceSummary(string? DeviceName, string? DeviceClass, string? ProductType,
    string? ProductVersion, string? BuildVersion, string? UniqueDeviceId);

public sealed record DeviceConnectionResult(UsbmuxdDeviceInfo Device, DeviceConnectionStatus Status,
    DeviceSummary? Summary = null, bool IsSslEnabled = false, string? ErrorCode = null, string? Detail = null);

/// <summary>Discovery describes the host enumeration. Each matching transport has its own connection result.</summary>
public sealed record DeviceConnectionReport(string Endpoint, DiscoveryStatus Status,
    IReadOnlyList<DeviceConnectionResult> Devices, string? Detail = null);

public static class DeviceConnectionDiagnostics
{
    /// <summary>
    /// Enumerates matching routes and checks Lockdown using existing trust only. Each discovery or device probe
    /// gets its own timeout (10 seconds by default). Caller cancellation propagates to the caller.
    /// </summary>
    public static async Task<DeviceConnectionReport> InspectAsync(UsbmuxdClient? usbmuxd = null,
        string? udid = null, UsbmuxdConnectionType? connectionType = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        usbmuxd ??= new UsbmuxdClient();
        string? serial = UsbmuxdClient.NormalizeUdid(udid);
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(10);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero, nameof(timeout));
        IReadOnlyList<UsbmuxdDeviceInfo> devices;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(limit);
            devices = await usbmuxd.ListDevicesAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(usbmuxd.Address, DiscoveryStatus.TimedOut, [], "Device enumeration timed out.");
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = ex is UsbmuxdException { InnerException: SocketException }
                ? DiscoveryStatus.DaemonUnavailable : DiscoveryStatus.Failed;
            return new(usbmuxd.Address, status, [], ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (devices.Count == 0)
        {
            return new(usbmuxd.Address, DiscoveryStatus.NoDevices, [], "The daemon reported no attached devices.");
        }
        var results = new List<DeviceConnectionResult>();
        foreach (var device in UsbmuxdClient.MatchingDevices(devices, serial, connectionType))
        {
            results.Add(await InspectDeviceAsync(usbmuxd, device, limit, cancellationToken).ConfigureAwait(false));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results.Count == 0
            ? new(usbmuxd.Address, DiscoveryStatus.NoMatch, [], "No route matches the requested UDID and connection type.")
            : new(usbmuxd.Address, DiscoveryStatus.DevicesFound, results);
    }

    private static async Task<DeviceConnectionResult> InspectDeviceAsync(UsbmuxdClient mux, UsbmuxdDeviceInfo device,
        TimeSpan limit, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit);
        var token = deadline.Token;
        try
        {
            await using var lockdown = await LockdownClient.ConnectAsync(mux, device, handshake: false, cancellationToken: token).ConfigureAwait(false);
            if (await lockdown.QueryTypeAsync(token).ConfigureAwait(false) != "com.apple.mobile.lockdown")
            {
                return new(device, DeviceConnectionStatus.LockdownUnavailable, Detail: "Unexpected Lockdown service type.");
            }

            var record = await mux.ReadPairRecordAsync(device.SerialNumber, token).ConfigureAwait(false);
            if (record != null)
            {
                await lockdown.StartSessionAsync(record, token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(lockdown.SessionId))
                {
                    return new(device, DeviceConnectionStatus.Failed, Detail: "StartSession returned no session ID.");
                }
            }

            PlistDictionary values;
            try
            {
                values = await lockdown.GetAllValuesAsync(cancellationToken: token).ConfigureAwait(false);
            }
            catch (LockdownException ex) when (record == null && ex.ErrorCode == "GetProhibited")
            {
                return new(device, DeviceConnectionStatus.PairingRequired, ErrorCode: ex.ErrorCode, Detail: ex.Message);
            }
            var summary = new DeviceSummary(values.GetString("DeviceName"), values.GetString("DeviceClass"),
                values.GetString("ProductType"), values.GetString("ProductVersion"), values.GetString("BuildVersion"),
                values.GetString("UniqueDeviceID"));
            return record == null
                ? new(device, DeviceConnectionStatus.PairingRequired, summary, Detail: "No local pair record for this device.")
                : new(device, DeviceConnectionStatus.Connected, summary, lockdown.IsSslEnabled);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(device, DeviceConnectionStatus.TimedOut, Detail: "Lockdown probe timed out.");
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
            {
                return new(device, DeviceConnectionStatus.TimedOut, Detail: "Lockdown probe timed out.");
            }
            var status = ex switch
            {
                LockdownException { ErrorCode: "PasswordProtected" or "DeviceLocked" or "EscrowLocked" } => DeviceConnectionStatus.DeviceLocked,
                LockdownException { ErrorCode: "PairingDialogResponsePending" or "MissingPairRecord" } => DeviceConnectionStatus.PairingRequired,
                LockdownException { ErrorCode: "InvalidHostID" or "InvalidPairRecord" or "UserDeniedPairing" or "PairingProhibitedOverThisConnection" } => DeviceConnectionStatus.PairingRejected,
                UsbmuxdException { ResultCode: UsbmuxdResultCode.BadDevice } => DeviceConnectionStatus.Disconnected,
                UsbmuxdException { ResultCode: UsbmuxdResultCode.ConnectionRefused } or
                    LockdownException { ErrorCode: "InvalidService" } => DeviceConnectionStatus.LockdownUnavailable,
                AuthenticationException or CryptographicException => DeviceConnectionStatus.AuthenticationFailed,
                _ => DeviceConnectionStatus.Failed,
            };
            string? code = (ex as LockdownException)?.ErrorCode ?? (ex as UsbmuxdException)?.ResultCode?.ToString();
            return new(device, status, ErrorCode: code, Detail: ex.Message);
        }
    }

    private static bool IsConnectionError(Exception ex) => ex is UsbmuxdException or LockdownException
        or IOException or SocketException or AuthenticationException or CryptographicException
        or FormatException or InvalidCastException or OverflowException or XmlException or InvalidDataException;
}
