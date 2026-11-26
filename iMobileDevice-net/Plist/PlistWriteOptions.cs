// <copyright file="PlistWriteOptions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Plist
{
    using System;

    /// <summary>
    /// Output formatting options for libplist (<c>plist_write_options_t</c>).
    /// </summary>
    [Flags]
    public enum PlistWriteOptions : int
    {
        None = 0,

        Compact = 1 << 0,

        PartialData = 1 << 1,

        NoNewline = 1 << 2,

        Indent = 1 << 3,

        Coerce = 1 << 4,
    }
}
