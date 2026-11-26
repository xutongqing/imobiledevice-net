// <copyright file="ReverseProxyErrorExtensions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    public static class ReverseProxyErrorExtensions
    {
        public static void ThrowOnError(this ReverseProxyError value)
        {
            if (value != ReverseProxyError.Success)
            {
                throw new ReverseProxyException(value);
            }
        }

        public static void ThrowOnError(this ReverseProxyError value, string message)
        {
            if (value != ReverseProxyError.Success)
            {
                throw new ReverseProxyException(value, message);
            }
        }

        public static bool IsError(this ReverseProxyError value)
        {
            return value != ReverseProxyError.Success;
        }
    }
}
