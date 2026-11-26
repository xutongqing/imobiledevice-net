// <copyright file="ReverseProxyClientHandle.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591

using System;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using Microsoft.Win32.SafeHandles;

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Represents a wrapper class for ReverseProxy handles.
    /// </summary>
    [SecurityPermission(SecurityAction.InheritanceDemand, UnmanagedCode = true)]
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
    public partial class ReverseProxyClientHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private string creationStackTrace;

        private ILibiMobileDevice api;

        protected ReverseProxyClientHandle() :
                base(true)
        {
            this.creationStackTrace = Environment.StackTrace;
        }

        protected ReverseProxyClientHandle(bool ownsHandle) :
                base(ownsHandle)
        {
            this.creationStackTrace = Environment.StackTrace;
        }

        public ILibiMobileDevice Api
        {
            get
            {
                return this.api;
            }
            set
            {
                this.api = value;
            }
        }

        public static ReverseProxyClientHandle Zero
        {
            get
            {
                return ReverseProxyClientHandle.DangerousCreate(System.IntPtr.Zero);
            }
        }

        [ReliabilityContractAttribute(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            Debug.WriteLine($"Releasing {this.GetType().Name} {this.handle} using {this.Api}. This object was created at {this.creationStackTrace}");
            Debug.Assert(this.Api != null, $"An instance of {this.GetType().Name} is being released but is not tied to an instance of the API.");
            return (this.Api.ReverseProxy.reverse_proxy_client_free(this.handle) == ReverseProxyError.Success);
        }

        public static ReverseProxyClientHandle DangerousCreate(System.IntPtr unsafeHandle, bool ownsHandle)
        {
            ReverseProxyClientHandle safeHandle = new ReverseProxyClientHandle(ownsHandle);
            safeHandle.SetHandle(unsafeHandle);
            return safeHandle;
        }

        public static ReverseProxyClientHandle DangerousCreate(System.IntPtr unsafeHandle)
        {
            return ReverseProxyClientHandle.DangerousCreate(unsafeHandle, true);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", this.handle, "ReverseProxyClientHandle");
        }

        public override bool Equals(object obj)
        {
            if (obj != null && obj.GetType() == typeof(ReverseProxyClientHandle))
            {
                return ((ReverseProxyClientHandle)obj).handle.Equals(this.handle);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return this.handle.GetHashCode();
        }

        public static bool operator ==(ReverseProxyClientHandle value1, ReverseProxyClientHandle value2)
        {
            if (object.Equals(value1, null) && object.Equals(value2, null))
            {
                return true;
            }

            if (object.Equals(value1, null) || object.Equals(value2, null))
            {
                return false;
            }

            return value1.handle == value2.handle;
        }

        public static bool operator !=(ReverseProxyClientHandle value1, ReverseProxyClientHandle value2)
        {
            if (object.Equals(value1, null) && object.Equals(value2, null))
            {
                return false;
            }

            if (object.Equals(value1, null) || object.Equals(value2, null))
            {
                return true;
            }

            return value1.handle != value2.handle;
        }
    }
}
