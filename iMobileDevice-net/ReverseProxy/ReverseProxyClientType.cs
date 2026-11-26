// <copyright file="ReverseProxyClientType.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Reverse proxy client type.
    /// </summary>
    public enum ReverseProxyClientType : int
    {
        /// <summary>
        /// Control connection (RP_TYPE_CTRL).
        /// </summary>
        Ctrl = 1,

        /// <summary>
        /// Proxy connection (RP_TYPE_CONN).
        /// </summary>
        Conn = 2,
    }
}
