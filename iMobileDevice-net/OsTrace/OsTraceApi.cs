// <copyright file="OsTraceApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    using System;
    using System.Collections.Concurrent;
    using iMobileDevice.iDevice;
    using iMobileDevice.Lockdown;
    using iMobileDevice.Plist;

    public partial class OsTraceApi : IOsTraceApi
    {
        private ILibiMobileDevice parent;

        private readonly ConcurrentDictionary<IntPtr, OsTraceActivityCallBack> activeActivityCallbacks =
            new ConcurrentDictionary<IntPtr, OsTraceActivityCallBack>();

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
            OsTraceError returnValue = OsTraceNativeMethods.ostrace_client_free(client);
            this.activeActivityCallbacks.TryRemove(client, out _);
            return returnValue;
        }

        public virtual OsTraceError ostrace_start_activity(OsTraceClientHandle client, PlistHandle options, OsTraceActivityCallBack callback, System.IntPtr userData)
        {
            IntPtr key = (client != null && !client.IsInvalid) ? client.DangerousGetHandle() : IntPtr.Zero;
            if (key != IntPtr.Zero && callback != null)
            {
                this.activeActivityCallbacks[key] = callback;
            }

            OsTraceError returnValue = OsTraceNativeMethods.ostrace_start_activity(client, options, callback, userData);
            if (returnValue != OsTraceError.Success && key != IntPtr.Zero)
            {
                this.activeActivityCallbacks.TryRemove(key, out _);
            }

            return returnValue;
        }

        public virtual OsTraceError ostrace_stop_activity(OsTraceClientHandle client)
        {
            OsTraceError returnValue = OsTraceNativeMethods.ostrace_stop_activity(client);
            if (client != null && !client.IsInvalid)
            {
                this.activeActivityCallbacks.TryRemove(client.DangerousGetHandle(), out _);
            }
            return returnValue;
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
            OsTraceError returnValue = OsTraceNativeMethods.ostrace_create_archive(client, options, callback, userData);
            GC.KeepAlive(callback);
            return returnValue;
        }
    }
}
