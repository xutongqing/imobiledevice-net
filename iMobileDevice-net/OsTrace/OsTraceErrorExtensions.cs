// <copyright file="OsTraceErrorExtensions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    public static class OsTraceErrorExtensions
    {
        public static void ThrowOnError(this OsTraceError value)
        {
            if (value != OsTraceError.Success)
            {
                throw new OsTraceException(value);
            }
        }

        public static void ThrowOnError(this OsTraceError value, string message)
        {
            if (value != OsTraceError.Success)
            {
                throw new OsTraceException(value, message);
            }
        }

        public static bool IsError(this OsTraceError value)
        {
            return value != OsTraceError.Success;
        }
    }
}
