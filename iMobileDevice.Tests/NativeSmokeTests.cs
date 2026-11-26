namespace iMobileDevice.Tests
{
    using System;
    using System.Collections.ObjectModel;
    using System.Runtime.InteropServices;
    using iMobileDevice.Afc;
    using iMobileDevice.BtPacketLogger;
    using iMobileDevice.FileRelay;
    using iMobileDevice.iDevice;
    using iMobileDevice.InstallationProxy;
    using iMobileDevice.NotificationProxy;
    using iMobileDevice.OsTrace;
    using iMobileDevice.Plist;
    using iMobileDevice.ReverseProxy;
    using iMobileDevice.Screenshotr;
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

            using (var dict = api.Plist.plist_new_dict())
            {
                Assert.False(dict.IsInvalid);

                var intNode = api.Plist.plist_new_int(-42);
                Assert.False(intNode.IsInvalid);
                Assert.Equal(PlistType.Int, api.Plist.plist_get_node_type(intNode));
                Assert.Equal(1, api.Plist.plist_int_val_is_negative(intNode));

                long val = 0;
                api.Plist.plist_get_int_val(intNode, ref val);
                Assert.Equal(-42, val);

                api.Plist.plist_dict_set_item(dict, "val", intNode);
                // Ownership of intNode transfers to dict in libplist.
                intNode.SetHandleAsInvalid();

                Assert.Equal(-42, api.Plist.plist_dict_get_int(dict, "val"));

                uint len = 0;
                Assert.Equal(PlistError.Success, api.Plist.plist_to_json(dict, out string json, ref len, 0));
                Assert.Contains("-42", json);

                uint xmlLen = 0;
                api.Plist.plist_to_xml(dict, out string xml, ref xmlLen);
                Assert.Contains("-42", xml);

                Assert.Equal(PlistError.Success, api.Plist.plist_from_json(json, (uint)json.Length, out PlistHandle parsed));
                using (parsed)
                {
                    Assert.Equal(-42, api.Plist.plist_dict_get_int(parsed, "val"));
                }
            }
        }

        [Fact]
        public void FixedNativeSignaturesAndHelpersWorkEndToEnd()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();

            // MobileSync anchors_new returns handle directly, anchors_free returns void
            using (var anchors = api.MobileSync.mobilesync_anchors_new("device-anchor-1", "computer-anchor-1"))
            {
                Assert.NotNull(anchors);
                Assert.False(anchors.IsInvalid);
                Assert.Same(api, anchors.Api);
            }

            // DebugServer size_t decode_string
            string encoded = "7175616D6F74696F6E";
            api.DebugServer.debugserver_decode_string(encoded, (UIntPtr)encoded.Length, out string decoded);
            Assert.Equal("quamotion", decoded);

            // InstallationProxy return attributes helper
            using (var opts = api.InstallationProxy.instproxy_client_options_new())
            {
                api.InstallationProxy.instproxy_client_options_set_return_attributes(opts, "CFBundleIdentifier", "Path");
                uint len = 0;
                Assert.Equal(PlistError.Success, api.Plist.plist_to_json(opts, out string json, ref len, 0));
                Assert.Contains("ReturnAttributes", json);
                Assert.Contains("CFBundleIdentifier", json);
                Assert.Contains("Path", json);
            }

            // MobileSync actions_add helper
            using (var actions = api.MobileSync.mobilesync_actions_new())
            {
                api.MobileSync.mobilesync_actions_add(actions, new[] { "com.apple.Contacts" }, true);
                uint len = 0;
                Assert.Equal(PlistError.Success, api.Plist.plist_to_json(actions, out string json, ref len, 0));
                Assert.Contains("SyncDeviceLinkEntityNamesKey", json);
                Assert.Contains("com.apple.Contacts", json);
                Assert.Contains("SyncDeviceLinkAllRecordsOfPulledEntityTypeSentKey", json);
            }

            // Input string-array APIs reject zero handle cleanly with InvalidArg (no crash/marshal error)
            Assert.Equal(
                FileRelayError.InvalidArg,
                api.FileRelay.file_relay_request_sources(FileRelayClientHandle.Zero, new ReadOnlyCollection<string>(new[] { "WiFi" }), out _));

            Assert.Equal(
                NotificationProxyError.InvalidArg,
                api.NotificationProxy.np_observe_notifications(NotificationProxyClientHandle.Zero, new ReadOnlyCollection<string>(new[] { NotificationProxyNotifications.AppInstalled })));

            Assert.Equal(
                InstallationProxyError.InvalidArg,
                api.InstallationProxy.instproxy_check_capabilities_match(InstallationProxyClientHandle.Zero, new ReadOnlyCollection<string>(new[] { "arm64" }), PlistHandle.Zero, out _));

            // Screenshotr fallback free works on upstream libimobiledevice without EntryPointNotFoundException
            Assert.Equal(ScreenshotrError.Success, api.Screenshotr.screenshotr_screenshot_free(IntPtr.Zero));
        }
    }
}
