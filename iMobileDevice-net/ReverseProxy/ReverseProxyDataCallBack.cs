// <copyright file="ReverseProxyDataCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Data callback function prototype for reverse proxy.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate void ReverseProxyDataCallBack(System.IntPtr client, ReverseProxyDataDirection direction, System.IntPtr buffer, uint length, System.IntPtr userData);
}
