// <copyright file="PlistException.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.Plist
{
#if !NETSTANDARD1_5
    [System.SerializableAttribute()]
#endif
    public class PlistException : System.Exception
    {
        private PlistError errorCode;

        public PlistException()
        {
        }

        public PlistException(PlistError error) :
                base(string.Format("A Plist error occurred. The error code was {0}", error))
        {
            this.errorCode = error;
        }

        public PlistException(PlistError error, string message) :
                base(string.Format("A Plist error occurred. {1}. The error code was {0}", error, message))
        {
            this.errorCode = error;
        }

        public PlistException(string message) :
                base(message)
        {
        }

        public PlistException(string message, System.Exception inner) :
                base(message, inner)
        {
        }

#if !NETSTANDARD1_5
        protected PlistException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) :
                base(info, context)
        {
        }
#endif

        public virtual PlistError ErrorCode
        {
            get
            {
                return this.errorCode;
            }
        }
    }
}
