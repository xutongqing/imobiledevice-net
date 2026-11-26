// <copyright file="BtPacketLoggerApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    using System;
    using System.Collections.Concurrent;
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;

    public partial class BtPacketLoggerApi : IBtPacketLoggerApi
    {
        private ILibiMobileDevice parent;

        private readonly ConcurrentDictionary<IntPtr, BtPacketLoggerReceiveCallBack> activeCallbacks =
            new ConcurrentDictionary<IntPtr, BtPacketLoggerReceiveCallBack>();

        public BtPacketLoggerApi(ILibiMobileDevice parent)
        {
            this.parent = parent;
        }

        public ILibiMobileDevice Parent
        {
            get
            {
                return this.parent;
            }
        }

        public virtual BtPacketLoggerError bt_packet_logger_client_new(iDeviceHandle device, LockdownServiceDescriptorHandle service, out BtPacketLoggerClientHandle client)
        {
            BtPacketLoggerError returnValue;
            returnValue = BtPacketLoggerNativeMethods.bt_packet_logger_client_new(device, service, out client);
            if (client != null)
            {
                client.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual BtPacketLoggerError bt_packet_logger_client_start_service(iDeviceHandle device, out BtPacketLoggerClientHandle client, string label)
        {
            BtPacketLoggerError returnValue;
            returnValue = BtPacketLoggerNativeMethods.bt_packet_logger_client_start_service(device, out client, label);
            if (client != null)
            {
                client.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual BtPacketLoggerError bt_packet_logger_client_free(System.IntPtr client)
        {
            BtPacketLoggerError returnValue = BtPacketLoggerNativeMethods.bt_packet_logger_client_free(client);
            this.activeCallbacks.TryRemove(client, out _);
            return returnValue;
        }

        public virtual BtPacketLoggerError bt_packet_logger_start_capture(BtPacketLoggerClientHandle client, BtPacketLoggerReceiveCallBack callback, System.IntPtr userData)
        {
            IntPtr key = (client != null && !client.IsInvalid) ? client.DangerousGetHandle() : IntPtr.Zero;
            if (key != IntPtr.Zero && callback != null)
            {
                this.activeCallbacks[key] = callback;
            }

            BtPacketLoggerError returnValue = BtPacketLoggerNativeMethods.bt_packet_logger_start_capture(client, callback, userData);
            if (returnValue != BtPacketLoggerError.Success && key != IntPtr.Zero)
            {
                this.activeCallbacks.TryRemove(key, out _);
            }

            return returnValue;
        }

        public virtual BtPacketLoggerError bt_packet_logger_stop_capture(BtPacketLoggerClientHandle client)
        {
            BtPacketLoggerError returnValue = BtPacketLoggerNativeMethods.bt_packet_logger_stop_capture(client);
            if (client != null && !client.IsInvalid)
            {
                this.activeCallbacks.TryRemove(client.DangerousGetHandle(), out _);
            }
            return returnValue;
        }

        public virtual BtPacketLoggerError bt_packet_logger_receive_with_timeout(BtPacketLoggerClientHandle client, byte[] data, uint size, ref uint received, uint timeout)
        {
            return BtPacketLoggerNativeMethods.bt_packet_logger_receive_with_timeout(client, data, size, ref received, timeout);
        }
    }
}
