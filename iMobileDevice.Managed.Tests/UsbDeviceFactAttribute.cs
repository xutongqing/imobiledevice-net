namespace iMobileDevice.Managed.Tests;

using Xunit;

public sealed class UsbDeviceFactAttribute : FactAttribute
{
    public UsbDeviceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IMOBILEDEVICE_RUN_DEVICE_E2E") != "1")
        {
            Skip = "Set IMOBILEDEVICE_RUN_DEVICE_E2E=1 to run against a paired USB device.";
        }
    }
}
