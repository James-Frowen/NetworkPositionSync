using UnityEngine;
using Conditional = System.Diagnostics.ConditionalAttribute;

namespace JamesFrowen.Logging
{
    public static class SimpleLogger
    {
        public static ILogger Logger = UnityEngine.Debug.unityLogger;

        [Conditional("TRACE")]
        public static void Trace(string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Log))
                Logger.Log(LogType.Log, $"[TRACE] {msg}");
        }

        [Conditional("DEBUG")]
        public static void Debug(string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Log))
                Logger.Log(LogType.Log, $"[DEBUG] {msg}");
        }

        [Conditional("DEBUG")]
        public static void DebugWarn(string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Warning))
                Logger.Log(LogType.Warning, $"[WARN] {msg}");
        }

        public static void Info(string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Log))
                Logger.Log(LogType.Log, $"[INFO] {msg}");
        }

        public static void Warn(string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Warning))
                Logger.Log(LogType.Warning, $"[WARN] {msg}");
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Warning))
                Logger.Log(LogType.Assert, $"[ASSERT] {msg}");
        }

        public static void Error(string msg)
        {
            if (Logger.IsLogTypeAllowed(LogType.Warning))
                Logger.Log(LogType.Error, $"[ERROR] {msg}");
        }
    }
}
