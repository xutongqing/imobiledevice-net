// <copyright file="FileRelayNativeMethods.Extensions.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.FileRelay
{
    using System.Collections.ObjectModel;
    using System.Runtime.InteropServices;
    using iMobileDevice.iDevice;

    public partial class FileRelayNativeMethods
    {
        public static FileRelayError file_relay_request_sources(FileRelayClientHandle client, ReadOnlyCollection<string> sources, out iDeviceConnectionHandle connection)
        {
            ICustomMarshaler sourcesMarshaler = NativeStringArrayMarshaler.GetInstance(null);
            System.IntPtr sourcesNative = sourcesMarshaler.MarshalManagedToNative(sources);
            try
            {
                return FileRelayNativeMethods.file_relay_request_sources(client, sourcesNative, out connection);
            }
            finally
            {
                sourcesMarshaler.CleanUpNativeData(sourcesNative);
            }
        }

        public static FileRelayError file_relay_request_sources_timeout(FileRelayClientHandle client, ReadOnlyCollection<string> sources, out iDeviceConnectionHandle connection, uint timeout)
        {
            ICustomMarshaler sourcesMarshaler = NativeStringArrayMarshaler.GetInstance(null);
            System.IntPtr sourcesNative = sourcesMarshaler.MarshalManagedToNative(sources);
            try
            {
                return FileRelayNativeMethods.file_relay_request_sources_timeout(client, sourcesNative, out connection, timeout);
            }
            finally
            {
                sourcesMarshaler.CleanUpNativeData(sourcesNative);
            }
        }
    }
}
