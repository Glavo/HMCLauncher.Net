namespace HMCLauncher
{
    internal sealed class LauncherMessages
    {
        private LauncherMessages(string errorSelfPath, string errorInvalidHmclJavaHome, string errorJavaNotFound)
        {
            ErrorSelfPath = errorSelfPath;
            ErrorInvalidHmclJavaHome = errorInvalidHmclJavaHome;
            ErrorJavaNotFound = errorJavaNotFound;
        }

        public string ErrorSelfPath { get; private set; }

        public string ErrorInvalidHmclJavaHome { get; private set; }

        public string ErrorJavaNotFound { get; private set; }

        public static LauncherMessages Create(int expectedJavaMajorVersion)
        {
            if (PlatformInterop.GetUserDefaultUILanguage() == 2052)
            {
                return new LauncherMessages(
                    "获取程序路径失败。",
                    "HMCL_JAVA_HOME 所指向的 Java 路径无效，请更新或删除该变量。\n",
                    "HMCL 需要 Java " + expectedJavaMajorVersion + " 或更高版本才能运行，点击“确定”开始下载 Java。\n请在安装 Java 完成后重新启动 HMCL。");
            }

            return new LauncherMessages(
                "Failed to get the exe path.",
                "The Java path specified by HMCL_JAVA_HOME is invalid. Please update it to a valid Java installation path or remove this environment variable.",
                "HMCL requires Java " + expectedJavaMajorVersion + " or later to run,\nClick 'OK' to start downloading java.\nPlease restart HMCL after installing Java.");
        }
    }
}
