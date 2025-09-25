using System;
using System.Text;

namespace Legend2Tool.WPF.Services
{
    public static class LogManager
    {
        private static readonly StringBuilder _logBuffer = new StringBuilder();
        private static Action<string>? _logCallback;

        public static void SetLogCallback(Action<string>? callback)
        {
            _logCallback = callback;
        }

        public static void AppendLog(string message)
        {
            _logBuffer.Append(message);

            // Call the callback if it's set
            _logCallback?.Invoke(message);
        }

        public static string GetLogText()
        {
            return _logBuffer.ToString();
        }

        public static void ClearLogs()
        {
            _logBuffer.Clear();
        }
    }
}