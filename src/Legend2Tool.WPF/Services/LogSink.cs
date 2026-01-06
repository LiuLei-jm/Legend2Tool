using Serilog.Core;
using Serilog.Events;

namespace Legend2Tool.WPF.Services
{
    public class LogSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) return;

            var message = logEvent.RenderMessage();
            if (logEvent.Exception != null)
            {
                message += Environment.NewLine + logEvent.Exception.ToString();
            }

            LogManager.AppendLog(message + Environment.NewLine);
        }

        public static LogSink Create()
        {
            return new LogSink();
        }
    }
}