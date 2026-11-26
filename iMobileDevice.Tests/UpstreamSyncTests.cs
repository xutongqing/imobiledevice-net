namespace iMobileDevice.Tests
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using iMobileDevice.Afc;
    using iMobileDevice.BtPacketLogger;
    using iMobileDevice.DebugServer;
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;
    using iMobileDevice.MobileImageMounter;
    using iMobileDevice.OsTrace;
    using iMobileDevice.Plist;
    using iMobileDevice.PropertyListService;
    using iMobileDevice.Recovery;
    using iMobileDevice.ReverseProxy;
    using iMobileDevice.Service;
    using iMobileDevice.Usbmuxd;
    using Xunit;

    public class UpstreamSyncTests
    {
        [Fact]
        public void LibiMobileDeviceExposesNewModules()
        {
            var api = new LibiMobileDevice();

            Assert.NotNull(api.BtPacketLogger);
            Assert.Same(api, api.BtPacketLogger.Parent);

            Assert.NotNull(api.OsTrace);
            Assert.Same(api, api.OsTrace.Parent);

            Assert.NotNull(api.ReverseProxy);
            Assert.Same(api, api.ReverseProxy.Parent);
        }

        [Fact]
        public void StructLayoutSizesMatchUpstreamHeaders()
        {
            Assert.Equal(12, Marshal.SizeOf<BtPacketLoggerHeader>());
            Assert.Equal(0x81, Marshal.SizeOf<OsTracePacketHeader>());
        }

        [Fact]
        public void NewHandlesZeroAndEqualityWork()
        {
            var subZero = iDeviceSubscriptionContextHandle.Zero;
            var sub1 = iDeviceSubscriptionContextHandle.DangerousCreate(new IntPtr(101), ownsHandle: false);
            var sub2 = iDeviceSubscriptionContextHandle.DangerousCreate(new IntPtr(101), ownsHandle: false);
            Assert.True(subZero.IsInvalid);
            Assert.False(subZero.IsClosed);
            Assert.True(sub1 == sub2);
            Assert.True(sub1.Equals(sub2));
            Assert.True(sub1 != subZero);

            var btZero = BtPacketLoggerClientHandle.Zero;
            var bt1 = BtPacketLoggerClientHandle.DangerousCreate(new IntPtr(102), ownsHandle: false);
            var bt2 = BtPacketLoggerClientHandle.DangerousCreate(new IntPtr(102), ownsHandle: false);
            Assert.True(btZero.IsInvalid);
            Assert.True(bt1 == bt2);
            Assert.True(bt1 != btZero);

            var osZero = OsTraceClientHandle.Zero;
            var os1 = OsTraceClientHandle.DangerousCreate(new IntPtr(103), ownsHandle: false);
            var os2 = OsTraceClientHandle.DangerousCreate(new IntPtr(103), ownsHandle: false);
            Assert.True(osZero.IsInvalid);
            Assert.True(os1 == os2);
            Assert.True(os1 != osZero);

            var rpZero = ReverseProxyClientHandle.Zero;
            var rp1 = ReverseProxyClientHandle.DangerousCreate(new IntPtr(104), ownsHandle: false);
            var rp2 = ReverseProxyClientHandle.DangerousCreate(new IntPtr(104), ownsHandle: false);
            Assert.True(rpZero.IsInvalid);
            Assert.True(rp1 == rp2);
            Assert.True(rp1 != rpZero);
        }

        [Fact]
        public void NewErrorEnumsAndExtensionsWork()
        {
            Assert.Equal(-5, (int)iDeviceError.ConnRefused);
            Assert.Equal(-6, (int)MobileImageMounterError.NotSupported);
            Assert.Equal(0, (int)LockdownCuPairingCbType.PinRequested);
            Assert.Equal(1, (int)LockdownCuPairingCbType.DeviceInfo);
            Assert.Equal(2, (int)LockdownCuPairingCbType.Error);

            Assert.Equal(1, (int)ReverseProxyClientType.Ctrl);
            Assert.Equal(2, (int)ReverseProxyClientType.Conn);
            Assert.Equal(1, (int)ReverseProxyStatus.Ready);
            Assert.Equal(6, (int)ReverseProxyStatus.Disconnected);
            Assert.Equal(1, (int)ReverseProxyDataDirection.Out);
            Assert.Equal(2, (int)ReverseProxyDataDirection.In);

            Assert.False(BtPacketLoggerError.Success.IsError());
            BtPacketLoggerError.Success.ThrowOnError();
            Assert.True(BtPacketLoggerError.Timeout.IsError());
            var btEx = Assert.Throws<BtPacketLoggerException>(() => BtPacketLoggerError.Timeout.ThrowOnError("timed out"));
            Assert.Equal(BtPacketLoggerError.Timeout, btEx.ErrorCode);
            Assert.Contains("timed out", btEx.Message);

            Assert.False(OsTraceError.Success.IsError());
            OsTraceError.Success.ThrowOnError();
            Assert.True(OsTraceError.RequestFailed.IsError());
            var osEx = Assert.Throws<OsTraceException>(() => OsTraceError.RequestFailed.ThrowOnError());
            Assert.Equal(OsTraceError.RequestFailed, osEx.ErrorCode);

            Assert.False(ReverseProxyError.Success.IsError());
            ReverseProxyError.Success.ThrowOnError();
            Assert.True(ReverseProxyError.MuxError.IsError());
            var rpEx = Assert.Throws<ReverseProxyException>(() => ReverseProxyError.MuxError.ThrowOnError("mux failed"));
            Assert.Equal(ReverseProxyError.MuxError, rpEx.ErrorCode);
            Assert.Contains("mux failed", rpEx.Message);

            Assert.Equal(-1, (int)PlistType.None);
            Assert.Equal(1, (int)PlistType.Int);
            Assert.Equal(1, (int)PlistType.Uint);
            Assert.Equal(10, (int)PlistType.Null);
            Assert.Equal(3, (int)PlistFormat.Json);
            Assert.Equal(4, (int)PlistFormat.Ostep);
            Assert.Equal(0xf014, (int)RecoveryMode.KPortDfuMode);

            Assert.False(PlistError.Success.IsError());
            PlistError.Success.ThrowOnError();
            Assert.True(PlistError.Parse.IsError());
            var plEx = Assert.Throws<PlistException>(() => PlistError.Parse.ThrowOnError("bad json"));
            Assert.Equal(PlistError.Parse, plEx.ErrorCode);
            Assert.Contains("bad json", plEx.Message);
        }

        [Theory]
        [InlineData(typeof(iDeviceNativeMethods), "idevice_events_subscribe")]
        [InlineData(typeof(iDeviceNativeMethods), "idevice_events_unsubscribe")]
        [InlineData(typeof(iDeviceNativeMethods), "idevice_get_device_version")]
        [InlineData(typeof(iDeviceNativeMethods), "idevice_strerror")]
        [InlineData(typeof(iDeviceNativeMethods), "libimobiledevice_version")]
        [InlineData(typeof(LockdownNativeMethods), "lockdownd_cu_pairing_create")]
        [InlineData(typeof(LockdownNativeMethods), "lockdownd_cu_send_request_and_get_reply")]
        [InlineData(typeof(LockdownNativeMethods), "lockdownd_get_value_cu")]
        [InlineData(typeof(LockdownNativeMethods), "lockdownd_pair_cu")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_mount_image_with_options")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_unmount_image")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_query_developer_mode_status")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_query_nonce")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_query_personalization_identifiers")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_query_personalization_manifest")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_roll_personalization_nonce")]
        [InlineData(typeof(MobileImageMounterNativeMethods), "mobile_image_mounter_roll_cryptex_nonce")]
        [InlineData(typeof(AfcNativeMethods), "afc_get_device_info_plist")]
        [InlineData(typeof(AfcNativeMethods), "afc_get_file_info_plist")]
        [InlineData(typeof(AfcNativeMethods), "afc_strerror")]
        [InlineData(typeof(DebugServerNativeMethods), "debugserver_client_set_receive_params")]
        [InlineData(typeof(PropertyListServiceNativeMethods), "property_list_service_get_service_client")]
        [InlineData(typeof(ServiceNativeMethods), "service_get_connection")]
        [InlineData(typeof(BtPacketLoggerNativeMethods), "bt_packet_logger_client_new")]
        [InlineData(typeof(BtPacketLoggerNativeMethods), "bt_packet_logger_client_start_service")]
        [InlineData(typeof(BtPacketLoggerNativeMethods), "bt_packet_logger_client_free")]
        [InlineData(typeof(BtPacketLoggerNativeMethods), "bt_packet_logger_start_capture")]
        [InlineData(typeof(BtPacketLoggerNativeMethods), "bt_packet_logger_stop_capture")]
        [InlineData(typeof(BtPacketLoggerNativeMethods), "bt_packet_logger_receive_with_timeout")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_client_new")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_client_start_service")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_client_free")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_start_activity")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_stop_activity")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_get_pid_list")]
        [InlineData(typeof(OsTraceNativeMethods), "ostrace_create_archive")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_create_with_service")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_create_with_port")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_free")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_start_proxy")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_set_status_callback")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_set_log_callback")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_client_set_data_callback", "imobiledevice")]
        [InlineData(typeof(ReverseProxyNativeMethods), "reverse_proxy_get_type", "imobiledevice")]
        [InlineData(typeof(PlistNativeMethods), "plist_new_int", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_new_unix_date", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_new_null", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_array_free_iter", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_free_iter", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_get_bool", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_get_int", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_get_uint", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_copy_item", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_copy_bool", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_copy_int", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_copy_uint", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_copy_data", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_dict_copy_string", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_get_int_val", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_get_unix_date_val", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_set_int_val", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_set_unix_date_val", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_to_json", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_to_json_with_options", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_to_openstep", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_to_openstep_with_options", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_from_json", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_from_openstep", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_read_from_file", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_write_to_string", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_write_to_stream", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_write_to_file", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_print", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_int_val_is_negative", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_int_val_compare", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_unix_date_val_compare", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_sort", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_mem_free", "plist")]
        [InlineData(typeof(PlistNativeMethods), "plist_set_debug", "plist")]
        [InlineData(typeof(PlistNativeMethods), "libplist_version", "plist")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_version", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_async_usb_control_transfer", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_async_usb_control_transfer_with_cancel", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_usb_interrupt_transfer", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_send_command_breq", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_command_is_breq", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_setenv_np", "irecovery")]
        [InlineData(typeof(RecoveryNativeMethods), "irecv_devices_add", "irecovery")]
        [InlineData(typeof(UsbmuxdNativeMethods), "libusbmuxd_version", "usbmuxd")]
        public void NativeMethodHasExpectedDllImport(Type nativeMethodsType, string entryPoint, string expectedLibrary = "imobiledevice")
        {
            var method = nativeMethodsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == entryPoint && m.GetCustomAttribute<DllImportAttribute>() != null);

            Assert.NotNull(method);

            var dllImport = method.GetCustomAttribute<DllImportAttribute>();
            Assert.NotNull(dllImport);
            Assert.Equal(expectedLibrary, dllImport.Value);
            Assert.Equal(entryPoint, dllImport.EntryPoint);
            Assert.Equal(CallingConvention.Cdecl, dllImport.CallingConvention);
        }
    }
}
