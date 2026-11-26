// <copyright file="OsTraceError.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    /// <summary>
    /// Error codes returned by the os_trace_relay service.
    /// </summary>
    public enum OsTraceError : int
    {
        Success = 0,

        InvalidArg = -1,

        MuxError = -2,

        SslError = -3,

        NotEnoughData = -4,

        Timeout = -5,

        PlistError = -6,

        RequestFailed = -7,

        UnknownError = -256,
    }
}
