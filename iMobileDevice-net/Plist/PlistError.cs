// <copyright file="PlistError.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Plist
{
    /// <summary>
    /// Error codes returned by libplist 2.3+ functions (<c>plist_err_t</c>).
    /// </summary>
    public enum PlistError : int
    {
        Success = 0,

        InvalidArg = -1,

        Format = -2,

        Parse = -3,

        NoMem = -4,

        Io = -5,

        CircularRef = -6,

        MaxNesting = -7,

        Unknown = -255,
    }
}
