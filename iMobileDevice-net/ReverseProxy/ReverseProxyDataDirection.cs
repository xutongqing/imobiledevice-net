// <copyright file="ReverseProxyDataDirection.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Reverse proxy data direction passed to <see cref="ReverseProxyDataCallBack"/>.
    /// </summary>
    public enum ReverseProxyDataDirection : int
    {
        /// <summary>
        /// Data going out to remote host (RP_DATA_DIRECTION_OUT).
        /// </summary>
        Out = 1,

        /// <summary>
        /// Data coming in from remote host (RP_DATA_DIRECTION_IN).
        /// </summary>
        In = 2,
    }
}
