// <copyright file="ReverseProxyException.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.ReverseProxy
{
    /// <summary>
    /// Represents an exception that occurred when interacting with the ReverseProxy API.
    /// </summary>
#if !NETSTANDARD1_5
    [System.SerializableAttribute()]
#endif
    public class ReverseProxyException : System.Exception
    {
        private ReverseProxyError errorCode;

        public ReverseProxyException()
        {
        }

        public ReverseProxyException(ReverseProxyError error) :
                base(string.Format("A ReverseProxy error occurred. The error code was {0}", error))
        {
            this.errorCode = error;
        }

        public ReverseProxyException(ReverseProxyError error, string message) :
                base(string.Format("A ReverseProxy error occurred. {1}. The error code was {0}", error, message))
        {
            this.errorCode = error;
        }

        public ReverseProxyException(string message) :
                base(message)
        {
        }

        public ReverseProxyException(string message, System.Exception inner) :
                base(message, inner)
        {
        }

#if !NETSTANDARD1_5
        protected ReverseProxyException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) :
                base(info, context)
        {
        }
#endif

        public virtual ReverseProxyError ErrorCode
        {
            get
            {
                return this.errorCode;
            }
        }
    }
}
