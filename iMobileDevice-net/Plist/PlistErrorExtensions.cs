// <copyright file="PlistErrorExtensions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Plist
{
    public static class PlistErrorExtensions
    {
        public static void ThrowOnError(this PlistError value)
        {
            if (value != PlistError.Success)
            {
                throw new PlistException(value);
            }
        }

        public static void ThrowOnError(this PlistError value, string message)
        {
            if (value != PlistError.Success)
            {
                throw new PlistException(value, message);
            }
        }

        public static bool IsError(this PlistError value)
        {
            return value != PlistError.Success;
        }
    }
}
