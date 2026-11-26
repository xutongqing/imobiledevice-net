namespace iMobileDevice.Tests
{
    using System;
    using System.Collections.ObjectModel;
    using System.Runtime.InteropServices;
    using System.Threading;
    using iMobileDevice.Afc;
    using iMobileDevice.BtPacketLogger;
    using iMobileDevice.FileRelay;
    using iMobileDevice.iDevice;
    using iMobileDevice.InstallationProxy;
    using iMobileDevice.Lockdown;
    using iMobileDevice.MobileImageMounter;
    using iMobileDevice.NotificationProxy;
    using iMobileDevice.OsTrace;
    using iMobileDevice.Plist;
    using iMobileDevice.Service;
    using Xunit;
    using Xunit.Abstractions;

    /// <summary>
    /// Strictly read-only end-to-end tests against a real USB-connected iOS device.
    /// Enabled only when IMOBILEDEVICE_RUN_DEVICE_E2E=1.
    /// </summary>
    public class DeviceE2ETests
    {
        private readonly ITestOutputHelper output;

        public DeviceE2ETests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static bool ShouldRun =>
            string.Equals(Environment.GetEnvironmentVariable("IMOBILEDEVICE_RUN_DEVICE_E2E"), "1", StringComparison.Ordinal);

        private static string GetFirstDeviceUdid(ILibiMobileDevice api)
        {
            int count = 0;
            var err = api.iDevice.idevice_get_device_list(out ReadOnlyCollection<string> udids, ref count);
            Assert.Equal(iDeviceError.Success, err);
            Assert.True(count > 0 && udids.Count > 0, "No iOS device detected via usbmuxd.");
            return udids[0];
        }

        [Fact]
        public void DeviceDiscoveryAndEventSubscriptionWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();

            string udid = GetFirstDeviceUdid(api);
            this.output.WriteLine($"Detected device UDID: {udid}");

            IntPtr devicesPtr = IntPtr.Zero;
            int extCount = 0;
            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_get_device_list_extended(ref devicesPtr, ref extCount));
            try
            {
                Assert.True(extCount > 0);
                this.output.WriteLine($"idevice_get_device_list_extended count: {extCount}");
            }
            finally
            {
                api.iDevice.idevice_device_list_extended_free(devicesPtr);
            }

            // Subscribe using an ephemeral delegate + force GC to validate callback pinning
            int addEvents = 0;
            using (var gotAdd = new ManualResetEventSlim(false))
            {
                iDeviceError subErr = api.iDevice.idevice_events_subscribe(
                    out iDeviceSubscriptionContextHandle ctx,
                    (ref iDeviceEvent ev, IntPtr userData) =>
                    {
                        if (ev.@event == iDeviceEventType.DeviceAdd)
                        {
                            Interlocked.Increment(ref addEvents);
                            gotAdd.Set();
                        }
                    },
                    IntPtr.Zero);

                Assert.Equal(iDeviceError.Success, subErr);
                using (ctx)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    Assert.True(gotAdd.Wait(TimeSpan.FromSeconds(5)), "Expected initial DeviceAdd event.");
                    this.output.WriteLine($"idevice_events_subscribe received DeviceAdd events: {addEvents}");
                }
            }
        }

        [Fact]
        public void LockdownInfoAndServiceDescriptorIdentifierWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();
            string udid = GetFirstDeviceUdid(api);

            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_new_with_options(out iDeviceHandle device, udid, (int)iDeviceOptions.LookupUsbmux));
            using (device)
            {
                Assert.Equal(LockdownError.Success, api.Lockdown.lockdownd_client_new_with_handshake(device, out LockdownClientHandle lockdown, "imobiledevice-net-e2e"));
                using (lockdown)
                {
                    foreach (string key in new[] { "DeviceName", "ProductType", "ProductVersion", "BuildVersion" })
                    {
                        if (api.Lockdown.lockdownd_get_value(lockdown, null, key, out PlistHandle node) == LockdownError.Success && node != null && !node.IsInvalid)
                        {
                            using (node)
                            {
                                api.Plist.plist_get_string_val(node, out string val);
                                this.output.WriteLine($"{key}: {val}");
                            }
                        }
                    }

                    Assert.Equal(LockdownError.Success, api.Lockdown.lockdownd_start_service(lockdown, "com.apple.afc", out LockdownServiceDescriptorHandle svc));
                    using (svc)
                    {
                        var descriptor = Marshal.PtrToStructure<LockdownServiceDescriptor>(svc.DangerousGetHandle());
                        string identifier = descriptor.identifier != IntPtr.Zero ? Marshal.PtrToStringAnsi(descriptor.identifier) : null;
                        this.output.WriteLine($"Started AFC service descriptor: port={descriptor.port}, ssl={(byte)descriptor.ssl_enabled}, identifier={identifier}");
                        Assert.True(descriptor.port > 0);
                        Assert.Equal("com.apple.afc", identifier);
                    }
                }
            }
        }

        [Fact]
        public void ServiceFactoryCallbackAndAfcReadOnlyWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();
            string udid = GetFirstDeviceUdid(api);

            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_new(out iDeviceHandle device, udid));
            using (device)
            {
                // Verify by-value ConstructorFunc ABI in service_client_factory_start_service
                bool ctorInvoked = false;
                IntPtr rawClient = IntPtr.Zero;
                int ctorErr = 0;

                ConstructorFunc ctor = (IntPtr devPtr, IntPtr svcPtr, ref IntPtr outClient) =>
                {
                    ctorInvoked = devPtr != IntPtr.Zero && svcPtr != IntPtr.Zero;
                    outClient = IntPtr.Zero;
                    // Return a distinct non-zero sentinel error so factory cleanly reports our callback ran without leaking an unmanaged handle
                    return 42;
                };

                ServiceError factoryErr = api.Service.service_client_factory_start_service(
                    device,
                    "com.apple.afc",
                    ref rawClient,
                    "imobiledevice-net-e2e",
                    ctor,
                    ref ctorErr);

                this.output.WriteLine($"service_client_factory_start_service: factoryErr={factoryErr}, ctorInvoked={ctorInvoked}, ctorErr={ctorErr}");
                Assert.True(ctorInvoked, "ConstructorFunc callback was not invoked by native libimobiledevice.");
                Assert.Equal(42, ctorErr);

                // Read-only AFC directory listing
                Assert.Equal(AfcError.Success, api.Afc.afc_client_start_service(device, out AfcClientHandle afc, "imobiledevice-net-e2e"));
                using (afc)
                {
                    Assert.Equal(AfcError.Success, api.Afc.afc_read_directory(afc, "/", out ReadOnlyCollection<string> entries));
                    Assert.NotNull(entries);
                    Assert.True(entries.Count > 0);
                    this.output.WriteLine($"AFC '/' entries ({entries.Count}): {string.Join(", ", entries)}");
                }
            }
        }

        [Fact]
        public void InstallationProxyReturnAttributesAndCapabilitiesMatchWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();
            string udid = GetFirstDeviceUdid(api);

            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_new(out iDeviceHandle device, udid));
            using (device)
            {
                Assert.Equal(InstallationProxyError.Success, api.InstallationProxy.instproxy_client_start_service(device, out InstallationProxyClientHandle instproxy, "imobiledevice-net-e2e"));
                using (instproxy)
                {
                    using (PlistHandle opts = api.InstallationProxy.instproxy_client_options_new())
                    {
                        api.InstallationProxy.instproxy_client_options_set_return_attributes(opts, "CFBundleIdentifier", "CFBundleDisplayName");
                        Assert.Equal(InstallationProxyError.Success, api.InstallationProxy.instproxy_browse(instproxy, opts, out PlistHandle apps));
                        using (apps)
                        {
                            uint appCount = api.Plist.plist_array_get_size(apps);
                            this.output.WriteLine($"instproxy_browse returned {appCount} apps with filtered ReturnAttributes");
                            Assert.True(appCount > 0);

                            using (PlistHandle first = api.Plist.plist_array_get_item(apps, 0))
                            {
                                uint jsonLen = 0;
                                Assert.Equal(PlistError.Success, api.Plist.plist_to_json(first, out string json, ref jsonLen, 0));
                                this.output.WriteLine($"Sample app[0]: {json}");
                                Assert.Contains("CFBundleIdentifier", json);
                                // Owned by apps array, do not free separately
                                first.SetHandleAsInvalid();
                            }
                        }
                    }

                    // Test fixed input ReadOnlyCollection<string> marshaling
                    var caps = new ReadOnlyCollection<string>(new[] { "arm64", "wifi" });
                    Assert.Equal(InstallationProxyError.Success, api.InstallationProxy.instproxy_check_capabilities_match(instproxy, caps, PlistHandle.Zero, out PlistHandle capResult));
                    using (capResult)
                    {
                        uint len = 0;
                        Assert.Equal(PlistError.Success, api.Plist.plist_to_json(capResult, out string json, ref len, 0));
                        this.output.WriteLine($"instproxy_check_capabilities_match: {json}");
                    }
                }
            }
        }

        [Fact]
        public void NotificationProxyObserveNotificationsWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();
            string udid = GetFirstDeviceUdid(api);

            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_new(out iDeviceHandle device, udid));
            using (device)
            {
                Assert.Equal(NotificationProxyError.Success, api.NotificationProxy.np_client_start_service(device, out NotificationProxyClientHandle np, "imobiledevice-net-e2e"));
                using (np)
                {
                    var specs = new ReadOnlyCollection<string>(new[]
                    {
                        NotificationProxyNotifications.AppInstalled,
                        NotificationProxyNotifications.AppUninstalled,
                        NotificationProxyNotifications.TimezoneChanged,
                    });

                    Assert.Equal(NotificationProxyError.Success, api.NotificationProxy.np_observe_notifications(np, specs));
                    this.output.WriteLine($"np_observe_notifications succeeded for {specs.Count} notification constants.");
                }
            }
        }

        [Fact]
        public void OsTracePidListAndLiveActivityStreamWorks()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();
            string udid = GetFirstDeviceUdid(api);

            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_new(out iDeviceHandle device, udid));
            using (device)
            {
                OsTraceError startErr = api.OsTrace.ostrace_client_start_service(device, out OsTraceClientHandle ostrace, "imobiledevice-net-e2e");
                this.output.WriteLine($"ostrace_client_start_service: {startErr}");
                Assert.Equal(OsTraceError.Success, startErr);

                using (ostrace)
                {
                    Assert.Equal(OsTraceError.Success, api.OsTrace.ostrace_get_pid_list(ostrace, out PlistHandle pidList));
                    using (pidList)
                    {
                        uint jsonLen = 0;
                        Assert.Equal(PlistError.Success, api.Plist.plist_to_json(pidList, out string json, ref jsonLen, 0));
                        this.output.WriteLine($"ostrace_get_pid_list JSON length: {json.Length} chars");
                        Assert.True(json.Length > 20);
                    }
                }

                // com.apple.os_trace_relay handles one request per service session, so open a fresh client for StartActivity
                Assert.Equal(OsTraceError.Success, api.OsTrace.ostrace_client_start_service(device, out OsTraceClientHandle ostraceStream, "imobiledevice-net-e2e"));
                using (ostraceStream)
                {
                    int packets = 0;
                    long totalBytes = 0;
                    using (var gotPacket = new ManualResetEventSlim(false))
                    {
                        using (PlistHandle opts = api.Plist.plist_new_dict())
                        {
                            OsTraceError actErr = api.OsTrace.ostrace_start_activity(
                                ostraceStream,
                                opts,
                                (IntPtr buf, UIntPtr len, IntPtr userData) =>
                                {
                                    Interlocked.Increment(ref packets);
                                    Interlocked.Add(ref totalBytes, (long)len.ToUInt64());
                                    gotPacket.Set();
                                },
                                IntPtr.Zero);

                            Assert.Equal(OsTraceError.Success, actErr);
                            try
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                GC.Collect();

                                gotPacket.Wait(TimeSpan.FromSeconds(3));
                                Thread.Sleep(500);
                            }
                            finally
                            {
                                Assert.Equal(OsTraceError.Success, api.OsTrace.ostrace_stop_activity(ostraceStream));
                            }
                        }
                    }

                    this.output.WriteLine($"ostrace_start_activity received {packets} packets ({totalBytes} bytes).");
                    Assert.True(packets > 0, "Expected at least one live OsTrace activity packet from the device.");
                }
            }
        }

        [Fact]
        public void BtPacketLoggerMobileImageMounterAndFileRelayChecksWork()
        {
            if (!ShouldRun)
            {
                return;
            }

            var api = new LibiMobileDevice();
            string udid = GetFirstDeviceUdid(api);

            Assert.Equal(iDeviceError.Success, api.iDevice.idevice_new(out iDeviceHandle device, udid));
            using (device)
            {
                // 1. BtPacketLogger
                BtPacketLoggerError btStart = api.BtPacketLogger.bt_packet_logger_client_start_service(device, out BtPacketLoggerClientHandle bt, "imobiledevice-net-e2e");
                this.output.WriteLine($"bt_packet_logger_client_start_service: {btStart}");
                if (btStart == BtPacketLoggerError.Success)
                {
                    using (bt)
                    {
                        int btPackets = 0;
                        BtPacketLoggerError capErr = api.BtPacketLogger.bt_packet_logger_start_capture(
                            bt,
                            (IntPtr data, ushort len, IntPtr userData) => Interlocked.Increment(ref btPackets),
                            IntPtr.Zero);
                        Assert.Equal(BtPacketLoggerError.Success, capErr);
                        try
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            Thread.Sleep(1000);
                        }
                        finally
                        {
                            Assert.Equal(BtPacketLoggerError.Success, api.BtPacketLogger.bt_packet_logger_stop_capture(bt));
                        }
                        this.output.WriteLine($"bt_packet_logger capture ran cleanly (packets={btPackets}).");
                    }
                }

                // 2. MobileImageMounter (read-only lookup + iOS 17+ personalized query APIs)
                MobileImageMounterError mimStart = api.MobileImageMounter.mobile_image_mounter_start_service(device, out MobileImageMounterClientHandle mim, "imobiledevice-net-e2e");
                this.output.WriteLine($"mobile_image_mounter_start_service: {mimStart}");
                if (mimStart == MobileImageMounterError.Success)
                {
                    using (mim)
                    {
                        foreach (string imgType in new[] { "Developer", "Personalized" })
                        {
                            MobileImageMounterError lookupErr = api.MobileImageMounter.mobile_image_mounter_lookup_image(mim, imgType, out PlistHandle lookupResult);
                            this.output.WriteLine($"mobile_image_mounter_lookup_image('{imgType}'): {lookupErr}");
                            if (lookupResult != null && !lookupResult.IsInvalid)
                            {
                                using (lookupResult)
                                {
                                    uint len = 0;
                                    api.Plist.plist_to_json(lookupResult, out string json, ref len, 0);
                                    this.output.WriteLine($"  lookup('{imgType}') result: {json}");
                                }
                            }
                        }

                        var devModeErr = api.MobileImageMounter.mobile_image_mounter_query_developer_mode_status(mim, out PlistHandle devModePlist);
                        this.output.WriteLine($"mobile_image_mounter_query_developer_mode_status: {devModeErr}");
                        if (devModeErr == MobileImageMounterError.Success && devModePlist != null && !devModePlist.IsInvalid)
                        {
                            using (devModePlist)
                            {
                                uint len = 0;
                                api.Plist.plist_to_json(devModePlist, out string json, ref len, 0);
                                this.output.WriteLine($"  devMode: {json}");
                            }
                        }

                        var idErr = api.MobileImageMounter.mobile_image_mounter_query_personalization_identifiers(mim, null, out PlistHandle idPlist);
                        this.output.WriteLine($"mobile_image_mounter_query_personalization_identifiers: {idErr}");
                        if (idErr == MobileImageMounterError.Success && idPlist != null && !idPlist.IsInvalid)
                        {
                            using (idPlist)
                            {
                                uint len = 0;
                                api.Plist.plist_to_json(idPlist, out string json, ref len, 0);
                                this.output.WriteLine($"  identifiers: {json}");
                            }
                        }

                        uint nonceSize = 0;
                        var nonceErr = api.MobileImageMounter.mobile_image_mounter_query_nonce(mim, "Personalized", out IntPtr noncePtr, ref nonceSize);
                        this.output.WriteLine($"mobile_image_mounter_query_nonce('Personalized'): {nonceErr}, size={nonceSize}");
                        if (noncePtr != IntPtr.Zero)
                        {
                            api.Plist.plist_mem_free(noncePtr);
                        }
                    }
                }

                // 3. FileRelay (Apple removed com.apple.mobile.file_relay on iOS 8+, so service start failure is expected on modern iOS)
                FileRelayError frStart = api.FileRelay.file_relay_client_start_service(device, out FileRelayClientHandle fr, "imobiledevice-net-e2e");
                this.output.WriteLine($"file_relay_client_start_service: {frStart}");
                if (frStart == FileRelayError.Success)
                {
                    using (fr)
                    {
                        var frErr = api.FileRelay.file_relay_request_sources_timeout(fr, new ReadOnlyCollection<string>(new[] { "WiFi" }), out iDeviceConnectionHandle conn, 2000);
                        this.output.WriteLine($"file_relay_request_sources_timeout: {frErr}");
                        conn?.Dispose();
                    }
                }
            }
        }
    }
}
