// <copyright file="BtPacketLoggerReceiveCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    /// <summary>
    /// Receives each HCI packet received from the device.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate void BtPacketLoggerReceiveCallBack(System.IntPtr data, ushort len, System.IntPtr userData);
}
