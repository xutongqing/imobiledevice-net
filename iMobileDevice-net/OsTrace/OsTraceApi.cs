// <copyright file="OsTraceApi.cs" company="Quamotion">
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

    public partial class OsTraceApi : IOsTraceApi
    {
        private ILibiMobileDevice parent;

        public OsTraceApi(ILibiMobileDevice parent)
        {
            this.parent = parent;
        }

        public ILibiMobileDevice Parent
        {
            get
            {
                return this.parent;
            }
        }

        public virtual OsTraceError ostrace_client_new(iDeviceHandle device, LockdownServiceDescriptorHandle service, out OsTraceClientHandle client)
        {
            OsTraceError returnValue;
            returnValue = OsTraceNativeMethods.ostrace_client_new(device, service, out client);
            if (client != null)
            {
                client.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual OsTraceError ostrace_client_start_service(iDeviceHandle device, out OsTraceClientHandle client, string label)
        {
            OsTraceError returnValue;
            returnValue = OsTraceNativeMethods.ostrace_client_start_service(device, out client, label);
            if (client != null)
            {
                client.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual OsTraceError ostrace_client_free(System.IntPtr client)
        {
            return OsTraceNativeMethods.ostrace_client_free(client);
        }

        public virtual OsTraceError ostrace_start_activity(OsTraceClientHandle client, PlistHandle options, OsTraceActivityCallBack callback, System.IntPtr userData)
        {
            return OsTraceNativeMethods.ostrace_start_activity(client, options, callback, userData);
        }

        public virtual OsTraceError ostrace_stop_activity(OsTraceClientHandle client)
        {
            return OsTraceNativeMethods.ostrace_stop_activity(client);
        }

        public virtual OsTraceError ostrace_get_pid_list(OsTraceClientHandle client, out PlistHandle list)
        {
            OsTraceError returnValue;
            returnValue = OsTraceNativeMethods.ostrace_get_pid_list(client, out list);
            if (list != null)
            {
                list.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual OsTraceError ostrace_create_archive(OsTraceClientHandle client, PlistHandle options, OsTraceArchiveWriteCallBack callback, System.IntPtr userData)
        {
            return OsTraceNativeMethods.ostrace_create_archive(client, options, callback, userData);
        }
    }
}
