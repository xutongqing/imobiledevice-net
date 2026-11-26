// <copyright file="LockdownCuPairingCallBack.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Lockdown
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Callback used to supply the pairing PIN during a CU pairing session,
    /// and to report device information and pairing error messages.
    /// </summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public delegate void LockdownCuPairingCallBack(LockdownCuPairingCbType cbType, System.IntPtr userData, System.IntPtr dataPtr, ref uint dataSize);
}
