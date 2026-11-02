using System;

namespace HelperManager
{
    // Everything logs through this instead of IModContext.Log directly, so
    // classes don't need the context passed around. GameModEntry sets the
    // handlers once at load. Messages are Serilog templates: a literal
    // {...} is treated as a placeholder (harmless when nothing matches).
    internal static class Log
    {
        public static Action<string> InfoHandler;
        public static Action<string> WarningHandler;
        public static Action<string> ErrorHandler;

        public static void Info(string message)
        {
            InfoHandler?.Invoke(message);
        }

        public static void Warning(string message)
        {
            WarningHandler?.Invoke(message);
        }

        public static void Error(string message)
        {
            ErrorHandler?.Invoke(message);
        }
    }
}
