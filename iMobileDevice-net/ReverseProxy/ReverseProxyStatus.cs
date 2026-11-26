// <copyright file="ReverseProxyStatus.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Reverse proxy status reported by <see cref="ReverseProxyStatusCallBack"/>.
    /// </summary>
    public enum ReverseProxyStatus : int
    {
        Ready = 1,

        Terminate = 2,

        ConnectReq = 3,

        ShutdownReq = 4,

        Connected = 5,

        Disconnected = 6,
    }
}
