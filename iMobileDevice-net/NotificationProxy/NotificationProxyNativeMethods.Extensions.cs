// <copyright file="NotificationProxyNativeMethods.Extensions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.NotificationProxy
{
    using System.Collections.ObjectModel;
    using System.Runtime.InteropServices;

    public partial class NotificationProxyNativeMethods
    {
        public static NotificationProxyError np_observe_notifications(NotificationProxyClientHandle client, ReadOnlyCollection<string> notificationSpec)
        {
            ICustomMarshaler marshaler = NativeStringArrayMarshaler.GetInstance(null);
            System.IntPtr notificationSpecNative = marshaler.MarshalManagedToNative(notificationSpec);
            try
            {
                return NotificationProxyNativeMethods.np_observe_notifications(client, notificationSpecNative);
            }
            finally
            {
                marshaler.CleanUpNativeData(notificationSpecNative);
            }
        }
    }
}
