// <copyright file="OsTraceException.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    /// <summary>
    /// Represents an exception that occurred when interacting with the OsTrace API.
    /// </summary>
#if !NETSTANDARD1_5
    [System.SerializableAttribute()]
#endif
    public class OsTraceException : System.Exception
    {
        private OsTraceError errorCode;

        public OsTraceException()
        {
        }

        public OsTraceException(OsTraceError error) :
                base(string.Format("An OsTrace error occurred. The error code was {0}", error))
        {
            this.errorCode = error;
        }

        public OsTraceException(OsTraceError error, string message) :
                base(string.Format("An OsTrace error occurred. {1}. The error code was {0}", error, message))
        {
            this.errorCode = error;
        }

        public OsTraceException(string message) :
                base(message)
        {
        }

        public OsTraceException(string message, System.Exception inner) :
                base(message, inner)
        {
        }

#if !NETSTANDARD1_5
        protected OsTraceException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) :
                base(info, context)
        {
        }
#endif

        public virtual OsTraceError ErrorCode
        {
            get
            {
                return this.errorCode;
            }
        }
    }
}
