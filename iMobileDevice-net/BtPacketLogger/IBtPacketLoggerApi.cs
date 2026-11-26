// <copyright file="IBtPacketLoggerApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;

    public partial interface IBtPacketLoggerApi
    {
        ILibiMobileDevice Parent
        {
            get;
        }

        BtPacketLoggerError bt_packet_logger_client_new(iDeviceHandle device, LockdownServiceDescriptorHandle service, out BtPacketLoggerClientHandle client);

        BtPacketLoggerError bt_packet_logger_client_start_service(iDeviceHandle device, out BtPacketLoggerClientHandle client, string label);

        BtPacketLoggerError bt_packet_logger_client_free(System.IntPtr client);

        BtPacketLoggerError bt_packet_logger_start_capture(BtPacketLoggerClientHandle client, BtPacketLoggerReceiveCallBack callback, System.IntPtr userData);

        BtPacketLoggerError bt_packet_logger_stop_capture(BtPacketLoggerClientHandle client);

        BtPacketLoggerError bt_packet_logger_receive_with_timeout(BtPacketLoggerClientHandle client, byte[] data, uint size, ref uint received, uint timeout);
    }
}
