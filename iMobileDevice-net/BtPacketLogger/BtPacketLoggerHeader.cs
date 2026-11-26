// <copyright file="BtPacketLoggerHeader.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Header structure for Bluetooth HCI packets returned by bt_packet_logger.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BtPacketLoggerHeader
    {
        public uint length;

        public uint ts_secs;

        public uint ts_usecs;
    }
}
