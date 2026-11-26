// <copyright file="BtPacketLoggerNativeMethods.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    using System.Runtime.InteropServices;
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;

    public partial class BtPacketLoggerNativeMethods
    {
        public const string LibraryName = "imobiledevice";

        public const string ServiceName = "com.apple.bluetooth.BTPacketLogger";

        public const int MaxPacketSize = 65535;

        static BtPacketLoggerNativeMethods()
        {
            LibraryResolver.EnsureRegistered();
        }

        /// <summary>
        /// Connects to the bt_packet_logger service on the specified device.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(BtPacketLoggerNativeMethods.LibraryName, EntryPoint = "bt_packet_logger_client_new", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern BtPacketLoggerError bt_packet_logger_client_new(iDeviceHandle device, LockdownServiceDescriptorHandle service, out BtPacketLoggerClientHandle client);

        /// <summary>
        /// Starts a new bt_packet_logger service on the specified device and connects to it.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(BtPacketLoggerNativeMethods.LibraryName, EntryPoint = "bt_packet_logger_client_start_service", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern BtPacketLoggerError bt_packet_logger_client_start_service(iDeviceHandle device, out BtPacketLoggerClientHandle client, [System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.LPStr)] string label);

        /// <summary>
        /// Disconnects a bt_packet_logger client from the device and frees up the client data.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(BtPacketLoggerNativeMethods.LibraryName, EntryPoint = "bt_packet_logger_client_free", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern BtPacketLoggerError bt_packet_logger_client_free(System.IntPtr client);

        /// <summary>
        /// Starts capturing the HCI interface from the device using a callback.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(BtPacketLoggerNativeMethods.LibraryName, EntryPoint = "bt_packet_logger_start_capture", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern BtPacketLoggerError bt_packet_logger_start_capture(BtPacketLoggerClientHandle client, BtPacketLoggerReceiveCallBack callback, System.IntPtr userData);

        /// <summary>
        /// Stops capturing the HCI interface from the device.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(BtPacketLoggerNativeMethods.LibraryName, EntryPoint = "bt_packet_logger_stop_capture", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern BtPacketLoggerError bt_packet_logger_stop_capture(BtPacketLoggerClientHandle client);

        /// <summary>
        /// Receives data using the given bt_packet_logger client with specified timeout.
        /// </summary>
        [System.Runtime.InteropServices.DllImportAttribute(BtPacketLoggerNativeMethods.LibraryName, EntryPoint = "bt_packet_logger_receive_with_timeout", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern BtPacketLoggerError bt_packet_logger_receive_with_timeout(BtPacketLoggerClientHandle client, byte[] data, uint size, ref uint received, uint timeout);
    }
}
