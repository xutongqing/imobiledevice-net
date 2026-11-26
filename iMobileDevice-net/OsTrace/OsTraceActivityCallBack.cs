// <copyright file="OsTraceActivityCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    /// <summary>
    /// Receives unparsed ostrace data from the ostrace service.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate void OsTraceActivityCallBack(System.IntPtr buf, System.UIntPtr len, System.IntPtr userData);
}
