namespace iMobileDevice.Managed.Usbmuxd;

public enum UsbmuxdConnectionType
{
    Unknown,
    Usb,
    Network,
}

public enum UsbmuxdResultCode
{
    Ok = 0,
    BadCommand = 1,
    BadDevice = 2,
    ConnectionRefused = 3,
    BadVersion = 6,
}

public sealed record UsbmuxdDeviceInfo(
    int DeviceId,
    string SerialNumber,
    int ProductId,
    UsbmuxdConnectionType ConnectionType,
    int? LocationId = null,
    byte[]? NetworkAddress = null);

public enum UsbmuxdEventKind
{
    Attached,
    Detached,
}

public sealed record UsbmuxdDeviceEvent(
    UsbmuxdEventKind Kind,
    int DeviceId,
    UsbmuxdDeviceInfo? Device);

public sealed class UsbmuxdException : Exception
{
    public UsbmuxdException(string message, UsbmuxdResultCode? resultCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ResultCode = resultCode;
    }

    public UsbmuxdResultCode? ResultCode { get; }
}
