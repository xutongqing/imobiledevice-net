// <copyright file="DebugServerCancelReceiveCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.DebugServer
{
    /// <summary>
    /// Callback invoked periodically while awaiting a response from debugserver.
    /// Returns a non-zero value to cancel waiting for a response.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate int DebugServerCancelReceiveCallBack();
}
