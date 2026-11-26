// <copyright file="BtPacketLoggerApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;

    public partial class BtPacketLoggerApi : IBtPacketLoggerApi
    {
        private ILibiMobileDevice parent;

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
            return BtPacketLoggerNativeMethods.bt_packet_logger_client_free(client);
        }

        public virtual BtPacketLoggerError bt_packet_logger_start_capture(BtPacketLoggerClientHandle client, BtPacketLoggerReceiveCallBack callback, System.IntPtr userData)
        {
            return BtPacketLoggerNativeMethods.bt_packet_logger_start_capture(client, callback, userData);
        }

        public virtual BtPacketLoggerError bt_packet_logger_stop_capture(BtPacketLoggerClientHandle client)
        {
            return BtPacketLoggerNativeMethods.bt_packet_logger_stop_capture(client);
        }

        public virtual BtPacketLoggerError bt_packet_logger_receive_with_timeout(BtPacketLoggerClientHandle client, byte[] data, uint size, ref uint received, uint timeout)
        {
            return BtPacketLoggerNativeMethods.bt_packet_logger_receive_with_timeout(client, data, size, ref received, timeout);
        }
    }
}
