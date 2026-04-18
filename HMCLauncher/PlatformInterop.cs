using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HMCLauncher
{
    internal enum ArchitectureKind
    {
        X86,
        X64,
        Arm64
    }

    internal sealed class SelfPathInfo
    {
        public SelfPathInfo(string fullPath, string directoryPath, string fileName)
        {
            FullPath = fullPath;
            DirectoryPath = directoryPath;
            FileName = fileName;
        }

        public string FullPath { get; private set; }

        public string DirectoryPath { get; private set; }

        public string FileName { get; private set; }
    }

    internal static class PlatformInterop
    {
        private const ushort ImageFileMachineAmd64 = 0x8664;
        private const ushort ImageFileMachineArm64 = 0xAA64;
        private const ushort ProcessorArchitectureAmd64 = 9;
        private const ushort ProcessorArchitectureArm64 = 12;

        public static bool AttachToParentConsole()
        {
            return NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
        }

        public static ArchitectureKind GetArchitecture()
        {
            IntPtr kernel32Module = NativeMethods.GetModuleHandle("kernel32.dll");
            if (kernel32Module != IntPtr.Zero)
            {
                IntPtr functionPointer = NativeMethods.GetProcAddress(kernel32Module, "IsWow64Process2");
                if (functionPointer != IntPtr.Zero)
                {
                    IsWow64Process2Delegate processDelegate =
                        (IsWow64Process2Delegate) Marshal.GetDelegateForFunctionPointer(functionPointer, typeof(IsWow64Process2Delegate));

                    using (Process currentProcess = Process.GetCurrentProcess())
                    {
                        ushort processMachine;
                        ushort nativeMachine;
                        if (processDelegate(currentProcess.Handle, out processMachine, out nativeMachine))
                        {
                            if (nativeMachine == ImageFileMachineArm64)
                            {
                                return ArchitectureKind.Arm64;
                            }

                            if (nativeMachine == ImageFileMachineAmd64)
                            {
                                return ArchitectureKind.X64;
                            }

                            return ArchitectureKind.X86;
                        }
                    }
                }
            }

            NativeMethods.SYSTEM_INFO systemInfo;
            NativeMethods.GetNativeSystemInfo(out systemInfo);
            if (systemInfo.ProcessorArchitecture == ProcessorArchitectureArm64)
            {
                return ArchitectureKind.Arm64;
            }

            if (systemInfo.ProcessorArchitecture == ProcessorArchitectureAmd64)
            {
                return ArchitectureKind.X64;
            }

            return ArchitectureKind.X86;
        }

        public static string GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public static string GetLauncherVersion()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "Unknown" : version.ToString();
        }

        public static ushort GetUserDefaultUILanguage()
        {
            return NativeMethods.GetUserDefaultUILanguage();
        }

        public static bool OpenUrl(string url)
        {
            IntPtr result = NativeMethods.ShellExecute(IntPtr.Zero, null, url, null, null, NativeMethods.SwShow);
            return result.ToInt64() > 32;
        }

        public static bool TryGetSelfPath(out SelfPathInfo selfPath)
        {
            selfPath = null;

            string executablePath = null;
            Assembly entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                executablePath = entryAssembly.Location;
            }

            if (string.IsNullOrEmpty(executablePath))
            {
                using (Process currentProcess = Process.GetCurrentProcess())
                {
                    if (currentProcess.MainModule != null)
                    {
                        executablePath = currentProcess.MainModule.FileName;
                    }
                }
            }

            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(executablePath);
            string directoryPath = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(directoryPath) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            selfPath = new SelfPathInfo(fullPath, directoryPath, fileName);
            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool IsWow64Process2Delegate(IntPtr processHandle, out ushort processMachine, out ushort nativeMachine);

        private static class NativeMethods
        {
            internal const int AttachParentProcess = -1;
            internal const int SwShow = 5;

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool AttachConsole(int dwProcessId);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
            internal static extern IntPtr GetProcAddress(IntPtr module, string procName);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            internal static extern IntPtr GetModuleHandle(string moduleName);

            [DllImport("kernel32.dll")]
            internal static extern void GetNativeSystemInfo(out SYSTEM_INFO systemInfo);

            [DllImport("kernel32.dll")]
            internal static extern ushort GetUserDefaultUILanguage();

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            internal static extern IntPtr ShellExecute(
                IntPtr hwnd,
                string operation,
                string file,
                string parameters,
                string directory,
                int showCommand);

            [StructLayout(LayoutKind.Sequential)]
            internal struct SYSTEM_INFO
            {
                internal ushort ProcessorArchitecture;
                internal ushort Reserved;
                internal uint PageSize;
                internal IntPtr MinimumApplicationAddress;
                internal IntPtr MaximumApplicationAddress;
                internal IntPtr ActiveProcessorMask;
                internal uint NumberOfProcessors;
                internal uint ProcessorType;
                internal uint AllocationGranularity;
                internal ushort ProcessorLevel;
                internal ushort ProcessorRevision;
            }
        }
    }
}
