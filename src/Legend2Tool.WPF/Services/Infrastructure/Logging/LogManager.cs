using System.Text;

namespace Legend2Tool.WPF.Services.Infrastructure.Logging
{
    public static class LogManager
    {
        private const int MaxLogLength = 1_000_000;
        private static readonly object _syncRoot = new();
        private static readonly StringBuilder _logBuffer = new StringBuilder();
        private static Action<string>? _logCallback;

        public static void SetLogCallback(Action<string>? callback)
        {
            lock (_syncRoot)
            {
                _logCallback = callback;
            }
        }

        public static void AppendLog(string message)
        {
            Action<string>? callback;
            lock (_syncRoot)
            {
                _logBuffer.Append(message);
                TrimBuffer();
                callback = _logCallback;
            }

            callback?.Invoke(message);
        }

        public static string GetLogText()
        {
            lock (_syncRoot)
            {
                return _logBuffer.ToString();
            }
        }

        public static void ClearLogs()
        {
            lock (_syncRoot)
            {
                _logBuffer.Clear();
            }
        }

        private static void TrimBuffer()
        {
            if (_logBuffer.Length <= MaxLogLength)
            {
                return;
            }

            int removeLength = _logBuffer.Length - MaxLogLength;
            int nextLineBreak = IndexOfNextLineBreak(removeLength);
            if (nextLineBreak >= 0)
            {
                removeLength = nextLineBreak + Environment.NewLine.Length;
            }

            _logBuffer.Remove(0, removeLength);
        }

        private static int IndexOfNextLineBreak(int startIndex)
        {
            for (int i = startIndex; i < _logBuffer.Length; i++)
            {
                if (_logBuffer[i] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
