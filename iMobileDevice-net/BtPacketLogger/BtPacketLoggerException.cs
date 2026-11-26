// <copyright file="BtPacketLoggerException.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.BtPacketLogger
{
    /// <summary>
    /// Represents an exception that occurred when interacting with the BtPacketLogger API.
    /// </summary>
#if !NETSTANDARD1_5
    [System.SerializableAttribute()]
#endif
    public class BtPacketLoggerException : System.Exception
    {
        private BtPacketLoggerError errorCode;

        public BtPacketLoggerException()
        {
        }

        public BtPacketLoggerException(BtPacketLoggerError error) :
                base(string.Format("A BtPacketLogger error occurred. The error code was {0}", error))
        {
            this.errorCode = error;
        }

        public BtPacketLoggerException(BtPacketLoggerError error, string message) :
                base(string.Format("A BtPacketLogger error occurred. {1}. The error code was {0}", error, message))
        {
            this.errorCode = error;
        }

        public BtPacketLoggerException(string message) :
                base(message)
        {
        }

        public BtPacketLoggerException(string message, System.Exception inner) :
                base(message, inner)
        {
        }

#if !NETSTANDARD1_5
        protected BtPacketLoggerException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) :
                base(info, context)
        {
        }
#endif

        public virtual BtPacketLoggerError ErrorCode
        {
            get
            {
                return this.errorCode;
            }
        }
    }
}
