using System;
using System.IO;

namespace NetPO.App
{
    public static class Logger
    {
        private static readonly string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "application_log.txt");
        private static readonly object lockObj = new object();

        public static void Log(string message)
        {
            try
            {
                lock (lockObj)
                {
                    File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // Fallback or ignore if logging fails
                System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }
}