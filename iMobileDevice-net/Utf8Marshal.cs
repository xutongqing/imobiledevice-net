using System;
using System.Runtime.InteropServices;
using System.Text;

namespace iMobileDevice
{
    /// <summary>
    /// Provides marshalling capabilities for UTF-8 strings.
    /// </summary>
    public static class Utf8Marshal
    {
        /// <summary>
        /// Converts a pointer to an UTF-8 string.
        /// </summary>
        /// <param name="value">
        /// The value to marshal.
        /// </param>
        /// <returns>
        /// A <see cref="string"/> which represents the underlying string.
        /// </returns>
        public static string PtrToStringUtf8(IntPtr value)
        {
            return PtrToStringUtf8(value, LibiMobileDevice.Instance);
        }

        /// <summary>
        /// Converts a pointer to an UTF-8 string.
        /// </summary>
        /// <param name="value">
        /// The value to marshal.
        /// </param>
        /// <param name="api">
        /// The libimobiledevice API to use when marshalling.
        /// </param>
        /// <returns>
        /// A <see cref="string"/> which represents the underlying string.
        /// </returns>
        public static string PtrToStringUtf8(IntPtr value, ILibiMobileDevice api)
        {
            if (value == IntPtr.Zero)
            {
                return null;
            }

            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }

#if !NETCOREAPP2_0 && !NETSTANDARD2_0 && !NET45
            return Marshal.PtrToStringUTF8(value);
#else
            int size = 0;
            while (Marshal.ReadByte(value, size) != 0)
            {
                size++;
            }

            if (size == 0)
            {
                return string.Empty;
            }

            byte[] data = new byte[size];
            Marshal.Copy(value, data, 0, size);
            return Encoding.UTF8.GetString(data);
#endif
        }

        /// <summary>
        /// Converts a value to an UTF-8 string, in native memory.
        /// </summary>
        /// <param name="value">
        /// The value to encode.
        /// </param>
        /// <returns>
        /// A pointer to the UTF-8 string in unmanaged memory. You must free this pointer
        /// using <see cref="Marshal.FreeHGlobal(IntPtr)"/>.
        /// </returns>
        public static IntPtr StringToHGlobalUtf8(string value)
        {
            if (value == null)
            {
                return IntPtr.Zero;
            }

            byte[] data = Encoding.UTF8.GetBytes(value + '\0');

            // Make sure the string is 0-terminated
            IntPtr handle = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, handle, data.Length);

            return handle;
        }
    }
}
