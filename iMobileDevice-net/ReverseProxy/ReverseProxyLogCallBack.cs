// <copyright file="ReverseProxyLogCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Log callback function prototype for reverse proxy.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate void ReverseProxyLogCallBack(System.IntPtr client, [System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.LPStr)] string logMsg, System.IntPtr userData);
}
