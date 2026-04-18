using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace HMCLauncher
{
    internal sealed class JavaDiscovery
    {
        private static readonly string[] VendorDirectories =
        {
            "Java",
            "Microsoft",
            "BellSoft",
            "Zulu",
            "Eclipse Foundation",
            "AdoptOpenJDK",
            "Semeru"
        };

        private readonly int _expectedJavaMajorVersion;
        private readonly string _javaExecutableName;
        private readonly RegistryView _registryView;
        private readonly List<JavaRuntimeCandidate> _candidates = new List<JavaRuntimeCandidate>();
        private readonly HashSet<string> _seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public JavaDiscovery(int expectedJavaMajorVersion, string javaExecutableName, ArchitectureKind architecture)
        {
            _expectedJavaMajorVersion = expectedJavaMajorVersion;
            _javaExecutableName = javaExecutableName;
            _registryView = architecture == ArchitectureKind.X86 ? RegistryView.Registry32 : RegistryView.Registry64;
        }

        public IList<JavaRuntimeCandidate> Candidates
        {
            get { return _candidates; }
        }

        public void SearchInDirectory(string baseDirectory)
        {
            LauncherLogger.Verbose(string.Format("Searching in directory: {0}", baseDirectory));

            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                return;
            }

            try
            {
                string[] directories = Directory.GetDirectories(baseDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    TryAdd(Path.Combine(directories[i], "bin", _javaExecutableName));
                }
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    LauncherLogger.Verbose(string.Format("Failed to enumerate directory: {0}", baseDirectory));
                    return;
                }

                throw;
            }
        }

        public void SearchInProgramFiles(string programFilesPath)
        {
            for (int i = 0; i < VendorDirectories.Length; i++)
            {
                SearchInDirectory(Path.Combine(programFilesPath, VendorDirectories[i]));
            }
        }

        public void SearchInRegistry(string subKeyPath)
        {
            LauncherLogger.Verbose(string.Format("Searching in registry key: HKEY_LOCAL_MACHINE\\{0}", subKeyPath));

            try
            {
                using (RegistryKey localMachine = OpenLocalMachineBaseKey())
                {
                    if (localMachine == null)
                    {
                        return;
                    }

                    using (RegistryKey javaRoot = localMachine.OpenSubKey(subKeyPath))
                    {
                        if (javaRoot == null)
                        {
                            return;
                        }

                        string[] subKeyNames = javaRoot.GetSubKeyNames();
                        for (int i = 0; i < subKeyNames.Length; i++)
                        {
                            using (RegistryKey versionKey = javaRoot.OpenSubKey(subKeyNames[i]))
                            {
                                if (versionKey == null)
                                {
                                    continue;
                                }

                                string javaHome = versionKey.GetValue("JavaHome") as string;
                                if (!string.IsNullOrEmpty(javaHome))
                                {
                                    TryAdd(Path.Combine(javaHome, "bin", _javaExecutableName));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    return;
                }

                throw;
            }
        }

        public void SearchInPath(string pathValue)
        {
            if (pathValue == null)
            {
                return;
            }

            string[] segments = pathValue.Split(';');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i].Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                string javaExecutablePath = Path.Combine(segment, _javaExecutableName);
                if (javaExecutablePath.IndexOf("\\Common Files\\Oracle\\Java\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LauncherLogger.Verbose(string.Format("Ignore Oracle Java {0}", javaExecutablePath));
                    continue;
                }

                LauncherLogger.Verbose("Checking " + javaExecutablePath);
                TryAdd(javaExecutablePath);
            }
        }

        public bool TryAdd(string javaExecutablePath)
        {
            if (string.IsNullOrEmpty(javaExecutablePath) || !File.Exists(javaExecutablePath))
            {
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(javaExecutablePath);
            }
            catch (Exception)
            {
                fullPath = javaExecutablePath;
            }

            if (_seenPaths.Contains(fullPath))
            {
                LauncherLogger.Verbose(string.Format("Ignore duplicate Java {0}", fullPath));
                return false;
            }

            Version version = JavaRuntimeCandidate.ReadVersion(fullPath);
            bool acceptable = version.Major >= _expectedJavaMajorVersion;
            LauncherLogger.Verbose(string.Format(
                "Found Java {0}, Version {1}{2}",
                fullPath,
                JavaRuntimeCandidate.ToDisplayString(version),
                acceptable ? string.Empty : ", Ignored"));

            if (!acceptable)
            {
                return false;
            }

            _seenPaths.Add(fullPath);
            _candidates.Add(new JavaRuntimeCandidate(fullPath, version));
            return true;
        }

        public void SortByVersion()
        {
            _candidates.Sort(delegate(JavaRuntimeCandidate left, JavaRuntimeCandidate right)
            {
                return left.Version.CompareTo(right.Version);
            });
        }

        private RegistryKey OpenLocalMachineBaseKey()
        {
            try
            {
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, _registryView);
            }
            catch (Exception ex)
            {
                if (ex is ArgumentException ||
                    ex is IOException ||
                    ex is PlatformNotSupportedException ||
                    ex is SecurityException ||
                    ex is UnauthorizedAccessException)
                {
                    return null;
                }

                throw;
            }
        }
    }

    internal sealed class JavaRuntimeCandidate
    {
        private static readonly Version UnknownVersion = new Version(0, 0, 0, 0);

        public JavaRuntimeCandidate(string executablePath, Version version)
        {
            ExecutablePath = executablePath;
            Version = version ?? UnknownVersion;
        }

        public string ExecutablePath { get; private set; }

        public Version Version { get; private set; }

        public static Version ReadVersion(string filePath)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(filePath);
                return new Version(
                    Math.Max(info.FileMajorPart, 0),
                    Math.Max(info.FileMinorPart, 0),
                    Math.Max(info.FileBuildPart, 0),
                    Math.Max(info.FilePrivatePart, 0));
            }
            catch (Exception)
            {
                return UnknownVersion;
            }
        }

        public static string ToDisplayString(Version version)
        {
            if (version == null || version == UnknownVersion)
            {
                return "Unknown";
            }

            return version.ToString(4);
        }
    }

    internal static class JavaProcessLauncher
    {
        private const string DefaultJvmOptions = "-Xmx1G -XX:MinHeapFreeRatio=5 -XX:MaxHeapFreeRatio=15";

        public static bool TryLaunch(string javaExecutablePath, LaunchContext context)
        {
            string arguments = BuildArguments(context);

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(javaExecutablePath, arguments);
                startInfo.UseShellExecute = false;
                startInfo.WorkingDirectory = context.WorkingDirectory;
                startInfo.ErrorDialog = false;

                using (Process process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        LauncherLogger.Info("Successfully launched HMCL with " + javaExecutablePath);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLogger.Verbose("Launch failure: " + ex.Message);
            }

            LauncherLogger.Info("Failed to launch HMCL with " + javaExecutablePath);
            return false;
        }

        private static string BuildArguments(LaunchContext context)
        {
            StringBuilder builder = new StringBuilder();
            if (context.JvmOptions != null)
            {
                builder.Append(context.JvmOptions);
            }
            else
            {
                builder.Append(DefaultJvmOptions);
            }

            builder.Append(" -jar \"");
            builder.Append(context.JarFileName);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
