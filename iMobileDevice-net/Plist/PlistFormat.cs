// <copyright file="PlistFormat.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Plist
{
    /// <summary>
    /// Formats supported by libplist (<c>plist_format_t</c>).
    /// </summary>
    public enum PlistFormat : int
    {
        None = 0,

        Xml = 1,

        Binary = 2,

        Json = 3,

        Ostep = 4,

        Print = 10,

        Limd = 11,

        Plutil = 12,
    }
}
