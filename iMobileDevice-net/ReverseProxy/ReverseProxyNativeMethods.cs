// <copyright file="ReverseProxyNativeMethods.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    using System.Runtime.InteropServices;
    using iMobileDevice.iDevice;

    public partial class ReverseProxyNativeMethods
    {
        public const string LibraryName = "imobiledevice";

        public const ushort DefaultPort = 1082;

        static ReverseProxyNativeMethods()
        {
            LibraryResolver.EnsureRegistered();
        }

        /// <summary>
        /// Create a reverse proxy client using com.apple.PurpleReverseProxy.Ctrl and com.apple.PurpleReverseProxy.Conn lockdown services.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_create_with_service", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern ReverseProxyError reverse_proxy_client_create_with_service(iDeviceHandle device, out ReverseProxyClientHandle client, [System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.LPStr)] string label);

        /// <summary>
        /// Create a reverse proxy client using an open port on the device.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_create_with_port", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern ReverseProxyError reverse_proxy_client_create_with_port(iDeviceHandle device, out ReverseProxyClientHandle client, ushort devicePort);

        /// <summary>
        /// Disconnects a reverse proxy client and frees up the client data.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_free", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern ReverseProxyError reverse_proxy_client_free(System.IntPtr client);

        /// <summary>
        /// Make an initialized reverse proxy client operational, i.e. start the actual proxy.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_start_proxy", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern ReverseProxyError reverse_proxy_client_start_proxy(ReverseProxyClientHandle client, int controlProtocolVersion);

        /// <summary>
        /// Set a status callback function.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_set_status_callback", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void reverse_proxy_client_set_status_callback(ReverseProxyClientHandle client, ReverseProxyStatusCallBack callback, System.IntPtr userData);

        /// <summary>
        /// Set a log callback function.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_set_log_callback", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void reverse_proxy_client_set_log_callback(ReverseProxyClientHandle client, ReverseProxyLogCallBack callback, System.IntPtr userData);

        /// <summary>
        /// Set a data callback function.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_client_set_data_callback", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void reverse_proxy_client_set_data_callback(ReverseProxyClientHandle client, ReverseProxyDataCallBack callback, System.IntPtr userData);

        /// <summary>
        /// Helper function to return the type of a given reverse proxy client.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(ReverseProxyNativeMethods.LibraryName, EntryPoint = "reverse_proxy_get_type", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern ReverseProxyClientType reverse_proxy_get_type(ReverseProxyClientHandle client);
    }
}
