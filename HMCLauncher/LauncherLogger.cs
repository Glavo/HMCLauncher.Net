using System;
using System.IO;

namespace HMCLauncher
{
    internal static class LauncherLogger
    {
        private static bool _consoleAttached;

        public static bool VerboseOutput { get; set; }

        public static void Initialize(bool consoleAttached)
        {
            _consoleAttached = consoleAttached;
            if (!_consoleAttached)
            {
                return;
            }

            try
            {
                StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput());
                standardOutput.AutoFlush = true;
                Console.SetOut(standardOutput);

                StreamWriter standardError = new StreamWriter(Console.OpenStandardError());
                standardError.AutoFlush = true;
                Console.SetError(standardError);

                Console.WriteLine();
            }
            catch (IOException)
            {
                _consoleAttached = false;
            }
        }

        public static void Info(string message)
        {
            if (!_consoleAttached)
            {
                return;
            }

            Console.WriteLine("[{0:HH:mm:ss.fff}] [HMCLauncher] {1}", DateTime.Now, message);
        }

        public static void Verbose(string message)
        {
            if (VerboseOutput)
            {
                Info(message);
            }
        }
    }
}
