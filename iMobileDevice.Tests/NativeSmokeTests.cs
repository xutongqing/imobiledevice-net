namespace iMobileDevice.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using iMobileDevice.Afc;
    using iMobileDevice.BtPacketLogger;
    using iMobileDevice.iDevice;
    using iMobileDevice.OsTrace;
    using iMobileDevice.Plist;
    using iMobileDevice.ReverseProxy;
    using Xunit;

    /// <summary>
    /// Smoke tests that load the real compiled native libraries (enabled when IMOBILEDEVICE_RUN_NATIVE_SMOKE=1).
    /// </summary>
    public class NativeSmokeTests
    {
        private static bool ShouldRun =>
            string.Equals(Environment.GetEnvironmentVariable("IMOBILEDEVICE_RUN_NATIVE_SMOKE"), "1", StringComparison.Ordinal);

        [Fact]
        public void AllNativeLibrariesLoadAndExposeNewVersionAndErrorApis()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();

            string imdVer = Marshal.PtrToStringAnsi(api.iDevice.libimobiledevice_version());
            Assert.False(string.IsNullOrWhiteSpace(imdVer));

            string plistVer = Marshal.PtrToStringAnsi(api.Plist.libplist_version());
            Assert.False(string.IsNullOrWhiteSpace(plistVer));

            string usbmuxdVer = Marshal.PtrToStringAnsi(api.Usbmuxd.libusbmuxd_version());
            Assert.False(string.IsNullOrWhiteSpace(usbmuxdVer));

            string irecvVer = Marshal.PtrToStringAnsi(api.Recovery.irecv_version());
            Assert.False(string.IsNullOrWhiteSpace(irecvVer));

            string connRefusedStr = Marshal.PtrToStringAnsi(api.iDevice.idevice_strerror(iDeviceError.ConnRefused));
            Assert.False(string.IsNullOrWhiteSpace(connRefusedStr));

            string afcOkStr = Marshal.PtrToStringAnsi(api.Afc.afc_strerror(AfcError.Success));
            Assert.False(string.IsNullOrWhiteSpace(afcOkStr));

            Assert.Equal(OsTraceError.InvalidArg, api.OsTrace.ostrace_client_free(IntPtr.Zero));
            Assert.Equal(BtPacketLoggerError.InvalidArg, api.BtPacketLogger.bt_packet_logger_client_free(IntPtr.Zero));
            Assert.Equal(ReverseProxyError.InvalidArg, api.ReverseProxy.reverse_proxy_client_free(IntPtr.Zero));
        }

        [Fact]
        public void LibPlistJsonAndSignedIntRoundTripWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();

            using (var intNode = api.Plist.plist_new_int(-42))
            {
                Assert.False(intNode.IsInvalid);
                Assert.Equal(PlistType.Int, api.Plist.plist_get_node_type(intNode));
                Assert.Equal(1, api.Plist.plist_int_val_is_negative(intNode));

                long val = 0;
                api.Plist.plist_get_int_val(intNode, ref val);
                Assert.Equal(-42, val);

                uint len = 0;
                Assert.Equal(PlistError.Success, api.Plist.plist_to_json(intNode, out string json, ref len, 0));
                Assert.Equal("-42", json.Trim());

                Assert.Equal(PlistError.Success, api.Plist.plist_from_json(json, (uint)json.Length, out PlistHandle parsed));
                using (parsed)
                {
                    long parsedVal = 0;
                    api.Plist.plist_get_int_val(parsed, ref parsedVal);
                    Assert.Equal(-42, parsedVal);
                }
            }
        }
    }
}
