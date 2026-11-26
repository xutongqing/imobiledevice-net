// <copyright file="LockdownCuPairingCbType.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Lockdown
{
    /// <summary>
    /// Callback types used in <see cref="LockdownCuPairingCallBack"/>.
    /// </summary>
    public enum LockdownCuPairingCbType : int
    {
        /// <summary>
        /// PIN requested: data_ptr is a char* buffer, and data_size points to the size of this buffer.
        /// </summary>
        PinRequested = 0,

        /// <summary>
        /// Device information available: data_ptr is a plist_t, and data_size is ignored.
        /// </summary>
        DeviceInfo = 1,

        /// <summary>
        /// Pairing error message available: data_ptr is a NULL-terminated char* buffer containing the error message.
        /// </summary>
        Error = 2,
    }
}
