// <copyright file="BtPacketLoggerError.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    /// <summary>
    /// Error codes returned by the bt_packet_logger service.
    /// </summary>
    public enum BtPacketLoggerError : int
    {
        Success = 0,

        InvalidArg = -1,

        MuxError = -2,

        SslError = -3,

        NotEnoughData = -4,

        Timeout = -5,

        UnknownError = -256,
    }
}
