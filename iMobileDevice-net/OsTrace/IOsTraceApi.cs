// <copyright file="IOsTraceApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;
    using iMobileDevice.Plist;

    public partial interface IOsTraceApi
    {
        ILibiMobileDevice Parent
        {
            get;
        }

        OsTraceError ostrace_client_new(iDeviceHandle device, LockdownServiceDescriptorHandle service, out OsTraceClientHandle client);

        OsTraceError ostrace_client_start_service(iDeviceHandle device, out OsTraceClientHandle client, string label);

        OsTraceError ostrace_client_free(System.IntPtr client);

        OsTraceError ostrace_start_activity(OsTraceClientHandle client, PlistHandle options, OsTraceActivityCallBack callback, System.IntPtr userData);

        OsTraceError ostrace_stop_activity(OsTraceClientHandle client);

        OsTraceError ostrace_get_pid_list(OsTraceClientHandle client, out PlistHandle list);

        OsTraceError ostrace_create_archive(OsTraceClientHandle client, PlistHandle options, OsTraceArchiveWriteCallBack callback, System.IntPtr userData);
    }
}
