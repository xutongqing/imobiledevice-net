// <copyright file="ReverseProxyApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    using System;
    using System.Collections.Concurrent;
    using iMobileDevice.iDevice;

    public partial class ReverseProxyApi : IReverseProxyApi
    {
        private ILibiMobileDevice parent;

        private readonly ConcurrentDictionary<IntPtr, ReverseProxyStatusCallBack> activeStatusCallbacks =
            new ConcurrentDictionary<IntPtr, ReverseProxyStatusCallBack>();

        private readonly ConcurrentDictionary<IntPtr, ReverseProxyLogCallBack> activeLogCallbacks =
            new ConcurrentDictionary<IntPtr, ReverseProxyLogCallBack>();

        private readonly ConcurrentDictionary<IntPtr, ReverseProxyDataCallBack> activeDataCallbacks =
            new ConcurrentDictionary<IntPtr, ReverseProxyDataCallBack>();

        public ReverseProxyApi(ILibiMobileDevice parent)
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

        public virtual ReverseProxyError reverse_proxy_client_create_with_service(iDeviceHandle device, out ReverseProxyClientHandle client, string label)
        {
            ReverseProxyError returnValue;
            returnValue = ReverseProxyNativeMethods.reverse_proxy_client_create_with_service(device, out client, label);
            if (client != null)
            {
                client.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual ReverseProxyError reverse_proxy_client_create_with_port(iDeviceHandle device, out ReverseProxyClientHandle client, ushort devicePort)
        {
            ReverseProxyError returnValue;
            returnValue = ReverseProxyNativeMethods.reverse_proxy_client_create_with_port(device, out client, devicePort);
            if (client != null)
            {
                client.Api = this.Parent;
            }
            return returnValue;
        }

        public virtual ReverseProxyError reverse_proxy_client_free(System.IntPtr client)
        {
            ReverseProxyError returnValue = ReverseProxyNativeMethods.reverse_proxy_client_free(client);
            this.activeStatusCallbacks.TryRemove(client, out _);
            this.activeLogCallbacks.TryRemove(client, out _);
            this.activeDataCallbacks.TryRemove(client, out _);
            return returnValue;
        }

        public virtual ReverseProxyError reverse_proxy_client_start_proxy(ReverseProxyClientHandle client, int controlProtocolVersion)
        {
            return ReverseProxyNativeMethods.reverse_proxy_client_start_proxy(client, controlProtocolVersion);
        }

        public virtual void reverse_proxy_client_set_status_callback(ReverseProxyClientHandle client, ReverseProxyStatusCallBack callback, System.IntPtr userData)
        {
            if (client != null && !client.IsInvalid)
            {
                IntPtr key = client.DangerousGetHandle();
                if (callback != null)
                {
                    this.activeStatusCallbacks[key] = callback;
                }
                else
                {
                    this.activeStatusCallbacks.TryRemove(key, out _);
                }
            }

            ReverseProxyNativeMethods.reverse_proxy_client_set_status_callback(client, callback, userData);
        }

        public virtual void reverse_proxy_client_set_log_callback(ReverseProxyClientHandle client, ReverseProxyLogCallBack callback, System.IntPtr userData)
        {
            if (client != null && !client.IsInvalid)
            {
                IntPtr key = client.DangerousGetHandle();
                if (callback != null)
                {
                    this.activeLogCallbacks[key] = callback;
                }
                else
                {
                    this.activeLogCallbacks.TryRemove(key, out _);
                }
            }

            ReverseProxyNativeMethods.reverse_proxy_client_set_log_callback(client, callback, userData);
        }

        public virtual void reverse_proxy_client_set_data_callback(ReverseProxyClientHandle client, ReverseProxyDataCallBack callback, System.IntPtr userData)
        {
            if (client != null && !client.IsInvalid)
            {
                IntPtr key = client.DangerousGetHandle();
                if (callback != null)
                {
                    this.activeDataCallbacks[key] = callback;
                }
                else
                {
                    this.activeDataCallbacks.TryRemove(key, out _);
                }
            }

            ReverseProxyNativeMethods.reverse_proxy_client_set_data_callback(client, callback, userData);
        }

        public virtual ReverseProxyClientType reverse_proxy_get_type(ReverseProxyClientHandle client)
        {
            return ReverseProxyNativeMethods.reverse_proxy_get_type(client);
        }
    }
}
