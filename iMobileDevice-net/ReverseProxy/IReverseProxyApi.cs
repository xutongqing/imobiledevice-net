// <copyright file="IReverseProxyApi.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    using iMobileDevice.iDevice;

    public partial interface IReverseProxyApi
    {
        ILibiMobileDevice Parent
        {
            get;
        }

        ReverseProxyError reverse_proxy_client_create_with_service(iDeviceHandle device, out ReverseProxyClientHandle client, string label);

        ReverseProxyError reverse_proxy_client_create_with_port(iDeviceHandle device, out ReverseProxyClientHandle client, ushort devicePort);

        ReverseProxyError reverse_proxy_client_free(System.IntPtr client);

        ReverseProxyError reverse_proxy_client_start_proxy(ReverseProxyClientHandle client, int controlProtocolVersion);

        void reverse_proxy_client_set_status_callback(ReverseProxyClientHandle client, ReverseProxyStatusCallBack callback, System.IntPtr userData);

        void reverse_proxy_client_set_log_callback(ReverseProxyClientHandle client, ReverseProxyLogCallBack callback, System.IntPtr userData);

        void reverse_proxy_client_set_data_callback(ReverseProxyClientHandle client, ReverseProxyDataCallBack callback, System.IntPtr userData);

        ReverseProxyClientType reverse_proxy_get_type(ReverseProxyClientHandle client);
    }
}
