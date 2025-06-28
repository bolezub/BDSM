using System;
using System.Collections.Generic;
using System.IO;

namespace BDSM
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public static class LoggingService
    {
        private static readonly string _logFilePath = Path.Combine(AppContext.BaseDirectory, "BDSM_Event.log");
        private static readonly object _logLock = new object();

        public static event Action<LogLevel>? OnNewLogEntry;

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            try
            {
                lock (_logLock)
                {
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level.ToString().ToUpper()}] {message}";
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }

                if (level == LogLevel.Error || level == LogLevel.Warning)
                {
                    OnNewLogEntry?.Invoke(level);
                }
            }
            catch
            {
                // If logging fails, there's nothing we can do.
            }
        }

        public static List<string> ReadLog()
        {
            lock (_logLock)
            {
                if (File.Exists(_logFilePath))
                {
                    return new List<string>(File.ReadAllLines(_logFilePath));
                }
                return new List<string>();
            }
        }

        public static void ClearLog()
        {
            lock (_logLock)
            {
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }
            }
        }
    }
}