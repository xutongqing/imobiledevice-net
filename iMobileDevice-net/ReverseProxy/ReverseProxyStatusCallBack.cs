// <copyright file="ReverseProxyStatusCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Status callback function prototype for reverse proxy.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate void ReverseProxyStatusCallBack(System.IntPtr client, ReverseProxyStatus status, [System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.LPStr)] string statusMsg, System.IntPtr userData);
}
