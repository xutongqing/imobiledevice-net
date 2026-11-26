// <copyright file="ReverseProxyError.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Error codes returned by the reverse_proxy service.
    /// </summary>
    public enum ReverseProxyError : int
    {
        Success = 0,

        InvalidArg = -1,

        PlistError = -2,

        MuxError = -3,

        SslError = -4,

        NotEnoughData = -5,

        Timeout = -6,

        UnknownError = -256,
    }
}
