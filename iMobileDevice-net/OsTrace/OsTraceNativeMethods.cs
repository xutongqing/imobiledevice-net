// <copyright file="OsTraceNativeMethods.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    using System.Runtime.InteropServices;
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;
    using iMobileDevice.Plist;

    public partial class OsTraceNativeMethods
    {
        public const string LibraryName = "imobiledevice";

        public const string ServiceName = "com.apple.os_trace_relay";

        static OsTraceNativeMethods()
        {
            LibraryResolver.EnsureRegistered();
        }

        /// <summary>
        /// Connects to the os_trace_relay service on the specified device.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_client_new", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_client_new(iDeviceHandle device, LockdownServiceDescriptorHandle service, out OsTraceClientHandle client);

        /// <summary>
        /// Starts a new os_trace_relay service on the specified device and connects to it.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_client_start_service", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_client_start_service(iDeviceHandle device, out OsTraceClientHandle client, [System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.LPStr)] string label);

        /// <summary>
        /// Disconnects an ostrace client from the device and frees up the client data.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_client_free", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_client_free(System.IntPtr client);

        /// <summary>
        /// Starts capturing OS trace activity data of the device using a callback.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_start_activity", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_start_activity(OsTraceClientHandle client, PlistHandle options, OsTraceActivityCallBack callback, System.IntPtr userData);

        /// <summary>
        /// Stops the ostrace activity.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_stop_activity", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_stop_activity(OsTraceClientHandle client);

        /// <summary>
        /// Returns a dictionary with all currently running processes on the device.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_get_pid_list", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_get_pid_list(OsTraceClientHandle client, out PlistHandle list);

        /// <summary>
        /// Creates a syslog archive.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(OsTraceNativeMethods.LibraryName, EntryPoint = "ostrace_create_archive", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern OsTraceError ostrace_create_archive(OsTraceClientHandle client, PlistHandle options, OsTraceArchiveWriteCallBack callback, System.IntPtr userData);
    }
}
