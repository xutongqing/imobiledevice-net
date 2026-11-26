// <copyright file="BtPacketLoggerErrorExtensions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    public static class BtPacketLoggerErrorExtensions
    {
        public static void ThrowOnError(this BtPacketLoggerError value)
        {
            if (value != BtPacketLoggerError.Success)
            {
                throw new BtPacketLoggerException(value);
            }
        }

        public static void ThrowOnError(this BtPacketLoggerError value, string message)
        {
            if (value != BtPacketLoggerError.Success)
            {
                throw new BtPacketLoggerException(value, message);
            }
        }

        public static bool IsError(this BtPacketLoggerError value)
        {
            return value != BtPacketLoggerError.Success;
        }
    }
}
