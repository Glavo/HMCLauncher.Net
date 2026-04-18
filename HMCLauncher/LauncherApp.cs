using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HMCLauncher
{
    internal static class LauncherApp
    {
        private const int ExpectedJavaMajorVersion = 17;

        public static int Run()
        {
            LauncherLogger.VerboseOutput = !string.Equals(
                PlatformInterop.GetEnvironmentVariable("HMCL_LAUNCHER_VERBOSE_OUTPUT") ?? string.Empty,
                "false",
                StringComparison.Ordinal);

            bool consoleAttached = PlatformInterop.AttachToParentConsole();
            LauncherLogger.Initialize(consoleAttached);

            string javaExecutableName = consoleAttached ? "java.exe" : "javaw.exe";
            ArchitectureKind architecture = PlatformInterop.GetArchitecture();
            LauncherMessages messages = LauncherMessages.Create(ExpectedJavaMajorVersion);

            SelfPathInfo selfPath;
            if (!PlatformInterop.TryGetSelfPath(out selfPath))
            {
                LauncherLogger.Info("Failed to get self path");
                MessageBox.Show(messages.ErrorSelfPath, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            LaunchContext context = new LaunchContext(
                selfPath.DirectoryPath,
                selfPath.FileName,
                PlatformInterop.GetEnvironmentVariable("HMCL_JAVA_OPTS"),
                architecture,
                javaExecutableName);

            LauncherLogger.Info(string.Format("*** HMCL Launcher {0} ***", PlatformInterop.GetLauncherVersion()));
            LauncherLogger.Info(string.Format("System Architecture: {0}", GetArchitectureDisplayName(architecture)));
            LauncherLogger.Info(string.Format("Working directory: {0}", context.WorkingDirectory));
            LauncherLogger.Info(string.Format("Exe File: {0}", Path.Combine(context.WorkingDirectory, context.JarFileName)));

            if (context.JvmOptions != null)
            {
                LauncherLogger.Info(string.Format("JVM Options: {0}", context.JvmOptions));
            }

            string hmclJavaHome = PlatformInterop.GetEnvironmentVariable("HMCL_JAVA_HOME");
            if (!string.IsNullOrEmpty(hmclJavaHome))
            {
                LauncherLogger.Info("HMCL_JAVA_HOME: " + hmclJavaHome);

                string hmclJavaExecutable = Path.Combine(hmclJavaHome, "bin", javaExecutableName);
                if (File.Exists(hmclJavaExecutable))
                {
                    if (JavaProcessLauncher.TryLaunch(hmclJavaExecutable, context))
                    {
                        return 0;
                    }
                }
                else
                {
                    LauncherLogger.Info(string.Format("Invalid HMCL_JAVA_HOME: {0}", hmclJavaHome));
                }

                MessageBox.Show(messages.ErrorInvalidHmclJavaHome, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            LauncherLogger.Verbose("HMCL_JAVA_HOME: Not Found");

            string bundledJavaExecutable = Path.Combine(
                context.WorkingDirectory,
                GetBundledJreDirectoryName(architecture),
                "bin",
                javaExecutableName);

            if (File.Exists(bundledJavaExecutable))
            {
                LauncherLogger.Info(string.Format("Bundled JRE: {0}", bundledJavaExecutable));
                if (JavaProcessLauncher.TryLaunch(bundledJavaExecutable, context))
                {
                    return 0;
                }
            }
            else
            {
                LauncherLogger.Verbose("Bundled JRE: Not Found");
            }

            string javaHome = PlatformInterop.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                LauncherLogger.Info("JAVA_HOME: " + javaHome);
            }
            else
            {
                LauncherLogger.Verbose("JAVA_HOME: Not Found");
            }

            JavaDiscovery discovery = new JavaDiscovery(ExpectedJavaMajorVersion, javaExecutableName, architecture);

            discovery.SearchInDirectory(Path.Combine(
                context.WorkingDirectory,
                ".hmcl",
                "java",
                GetHmclJavaDirectoryName(architecture)));

            if (!string.IsNullOrEmpty(javaHome))
            {
                LauncherLogger.Verbose("Checking JAVA_HOME");

                string javaHomeExecutable = Path.Combine(javaHome, "bin", javaExecutableName);
                if (File.Exists(javaHomeExecutable))
                {
                    discovery.TryAdd(javaHomeExecutable);
                }
                else
                {
                    LauncherLogger.Info(string.Format(
                        "JAVA_HOME is set to {0}, but the Java executable {1} does not exist",
                        javaHome,
                        javaHomeExecutable));
                }
            }

            string appDataPath = PlatformInterop.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appDataPath))
            {
                discovery.SearchInDirectory(Path.Combine(
                    appDataPath,
                    ".hmcl",
                    "java",
                    GetHmclJavaDirectoryName(architecture)));
            }

            string paths = PlatformInterop.GetEnvironmentVariable("PATH");
            if (paths != null)
            {
                LauncherLogger.Verbose("Searching in PATH");
                discovery.SearchInPath(paths);
            }
            else
            {
                LauncherLogger.Info("PATH: Not Found");
            }

            string programFilesPath = GetProgramFilesPath(architecture);
            if (!string.IsNullOrEmpty(programFilesPath))
            {
                discovery.SearchInProgramFiles(programFilesPath);
            }
            else
            {
                LauncherLogger.Info("Failed to obtain the path to Program Files");
            }

            discovery.SearchInRegistry(@"SOFTWARE\JavaSoft\JDK");
            discovery.SearchInRegistry(@"SOFTWARE\JavaSoft\JRE");

            if (discovery.Candidates.Count == 0)
            {
                LauncherLogger.Info("No Java runtime found.");
            }
            else
            {
                discovery.SortByVersion();

                if (LauncherLogger.VerboseOutput)
                {
                    StringBuilder builder = new StringBuilder();
                    builder.Append("Found Java runtimes:");
                    for (int i = 0; i < discovery.Candidates.Count; i++)
                    {
                        JavaRuntimeCandidate candidate = discovery.Candidates[i];
                        builder.AppendLine();
                        builder.Append("  - ");
                        builder.Append(candidate.ExecutablePath);
                        builder.Append(", Version ");
                        builder.Append(JavaRuntimeCandidate.ToDisplayString(candidate.Version));
                    }

                    LauncherLogger.Info(builder.ToString());
                }

                for (int i = discovery.Candidates.Count - 1; i >= 0; i--)
                {
                    if (JavaProcessLauncher.TryLaunch(discovery.Candidates[i].ExecutablePath, context))
                    {
                        return 0;
                    }
                }
            }

            if (MessageBox.Show(messages.ErrorJavaNotFound, string.Empty, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            {
                PlatformInterop.OpenUrl(GetDownloadLink(architecture));
            }

            return 1;
        }

        private static string GetArchitectureDisplayName(ArchitectureKind architecture)
        {
            switch (architecture)
            {
                case ArchitectureKind.Arm64:
                    return "arm64";
                case ArchitectureKind.X64:
                    return "x86-64";
                default:
                    return "x86";
            }
        }

        private static string GetBundledJreDirectoryName(ArchitectureKind architecture)
        {
            switch (architecture)
            {
                case ArchitectureKind.Arm64:
                    return "jre-arm64";
                case ArchitectureKind.X64:
                    return "jre-x64";
                default:
                    return "jre-x86";
            }
        }

        private static string GetHmclJavaDirectoryName(ArchitectureKind architecture)
        {
            switch (architecture)
            {
                case ArchitectureKind.Arm64:
                    return "windows-arm64";
                case ArchitectureKind.X64:
                    return "windows-x86_64";
                default:
                    return "windows-x86";
            }
        }

        private static string GetProgramFilesPath(ArchitectureKind architecture)
        {
            switch (architecture)
            {
                case ArchitectureKind.Arm64:
                case ArchitectureKind.X64:
                    return PlatformInterop.GetEnvironmentVariable("ProgramW6432");
                default:
                    return PlatformInterop.GetEnvironmentVariable("ProgramFiles");
            }
        }

        private static string GetDownloadLink(ArchitectureKind architecture)
        {
            switch (architecture)
            {
                case ArchitectureKind.Arm64:
                    return "https://docs.hmcl.net/downloads/windows/arm64.html";
                case ArchitectureKind.X64:
                    return "https://docs.hmcl.net/downloads/windows/x86_64.html";
                default:
                    return "https://docs.hmcl.net/downloads/windows/x86.html";
            }
        }
    }

    internal sealed class LaunchContext
    {
        public LaunchContext(string workingDirectory, string jarFileName, string jvmOptions, ArchitectureKind architecture, string javaExecutableName)
        {
            WorkingDirectory = workingDirectory;
            JarFileName = jarFileName;
            JvmOptions = jvmOptions;
            Architecture = architecture;
            JavaExecutableName = javaExecutableName;
        }

        public string WorkingDirectory { get; private set; }

        public string JarFileName { get; private set; }

        public string JvmOptions { get; private set; }

        public ArchitectureKind Architecture { get; private set; }

        public string JavaExecutableName { get; private set; }
    }
}
