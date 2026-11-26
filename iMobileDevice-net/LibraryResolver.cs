using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace iMobileDevice
{
    internal static class LibraryResolver
    {
        static LibraryResolver()
        {
#if !NETCOREAPP2_0 && !NETSTANDARD2_0 && !NET45
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), DllImportResolver);
#endif
        }

        public static void EnsureRegistered()
        {
            // Dummy call to trigger the static constructor
        }

#if !NETCOREAPP2_0 && !NETSTANDARD2_0 && !NET45
        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == Plist.PlistNativeMethods.LibraryName)
            {
                return LoadPlistLibrary(assembly, searchPath);
            }

            if (libraryName == Usbmuxd.UsbmuxdNativeMethods.LibraryName)
            {
                return LoadUsbmuxdLibrary(assembly, searchPath);
            }

            if (libraryName == iDevice.iDeviceNativeMethods.LibraryName)
            {
                return LoadMobileDeviceLibrary(assembly, searchPath);
            }

            if (libraryName == iDeviceActivation.iDeviceActivationNativeMethods.LibraryName)
            {
                return LoadDeviceActivationLibrary(assembly, searchPath);
            }

            if (libraryName == Recovery.RecoveryNativeMethods.LibraryName)
            {
                return LoadRecoveryLibrary(assembly, searchPath);
            }

            return IntPtr.Zero;
        }

        private static IntPtr TryLoadCandidates(Assembly assembly, DllImportSearchPath? searchPath, params string[] candidates)
        {
            foreach (var name in candidates)
            {
                if (NativeLibrary.TryLoad(name, assembly, searchPath, out IntPtr lib))
                {
                    return lib;
                }
            }

            string baseDir = AppContext.BaseDirectory;
            string rid = GetRuntimeIdentifier();
            string runtimeNativeDir = rid != null ? Path.Combine(baseDir, "runtimes", rid, "native") : null;

            foreach (var name in candidates)
            {
                string directPath = Path.Combine(baseDir, name);
                if (File.Exists(directPath) && NativeLibrary.TryLoad(directPath, out IntPtr lib))
                {
                    return lib;
                }

                if (runtimeNativeDir != null)
                {
                    string ridPath = Path.Combine(runtimeNativeDir, name);
                    if (File.Exists(ridPath) && NativeLibrary.TryLoad(ridPath, out lib))
                    {
                        return lib;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static string GetRuntimeIdentifier()
        {
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => null,
            };

            if (arch == null)
            {
                return null;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win-" + arch;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "linux-" + arch;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "osx-" + arch;
            }

            return null;
        }

        private static IntPtr LoadPlistLibrary(Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libplist-2.0-12.dll",
                    "libplist-2.0-4.dll",
                    "libplist-2.0-3.dll",
                    "libplist-2.0.dll",
                    "plist.dll");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libplist-2.0.so.12",
                    "libplist-2.0.so.4",
                    "libplist-2.0.so.3",
                    "libplist-2.0.so",
                    "libplist.so.3",
                    "libplist.so");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libplist-2.0.12.dylib",
                    "libplist-2.0.4.dylib",
                    "libplist-2.0.dylib",
                    "libplist.dylib");
            }

            return IntPtr.Zero;
        }

        private static IntPtr LoadUsbmuxdLibrary(Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libusbmuxd-2.0-7.dll",
                    "libusbmuxd-2.0-6.dll",
                    "libusbmuxd-2.0.dll",
                    "usbmuxd.dll");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libusbmuxd-2.0.so.7",
                    "libusbmuxd-2.0.so.6",
                    "libusbmuxd-2.0.so",
                    "libusbmuxd.so.6",
                    "libusbmuxd.so.4",
                    "libusbmuxd.so");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libusbmuxd-2.0.7.dylib",
                    "libusbmuxd-2.0.dylib",
                    "libusbmuxd.dylib");
            }

            return IntPtr.Zero;
        }

        private static IntPtr LoadMobileDeviceLibrary(Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libimobiledevice-1.0-6.dll",
                    "libimobiledevice-1.0.dll",
                    "imobiledevice.dll");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libimobiledevice-1.0.so.6",
                    "libimobiledevice-1.0.so",
                    "libimobiledevice.so.6",
                    "libimobiledevice.so");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libimobiledevice-1.0.6.dylib",
                    "libimobiledevice-1.0.dylib",
                    "libimobiledevice.dylib");
            }

            return IntPtr.Zero;
        }

        private static IntPtr LoadDeviceActivationLibrary(Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libideviceactivation-1.0-2.dll",
                    "libideviceactivation-1.0.dll",
                    "ideviceactivation.dll");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libideviceactivation-1.0.so.2",
                    "libideviceactivation-1.0.so",
                    "libideviceactivation.so.2",
                    "libideviceactivation.so");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libideviceactivation-1.0.2.dylib",
                    "libideviceactivation-1.0.dylib",
                    "libideviceactivation.dylib");
            }

            return IntPtr.Zero;
        }

        private static IntPtr LoadRecoveryLibrary(Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libirecovery-1.0-5.dll",
                    "libirecovery-1.0-4.dll",
                    "libirecovery-1.0-3.dll",
                    "libirecovery-1.0.dll",
                    "irecovery.dll");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libirecovery-1.0.so.5",
                    "libirecovery-1.0.so.4",
                    "libirecovery-1.0.so.3",
                    "libirecovery-1.0.so",
                    "libirecovery.so");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryLoadCandidates(
                    assembly,
                    searchPath,
                    "libirecovery-1.0.5.dylib",
                    "libirecovery-1.0.dylib",
                    "libirecovery.dylib");
            }

            return IntPtr.Zero;
        }
#endif
    }
}
