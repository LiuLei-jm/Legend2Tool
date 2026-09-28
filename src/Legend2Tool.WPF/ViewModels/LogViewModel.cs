using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Legend2Tool.WPF.Services.Infrastructure.Logging;
using Serilog;
using System.Windows;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class LogViewModel : ViewModelBase
    {
        private readonly ILogger _logger;

        public string Head { get; } = "日志查看";

        [ObservableProperty]
        private string _logText = string.Empty;

        public LogViewModel(ILogger logger)
        {
            _logger = logger;

            // Set the callback to update the LogText property
            LogManager.SetLogCallback(OnLogReceived);

            // Initialize with existing logs
            LogText = LogManager.GetLogText();
        }

        private void OnLogReceived(string logMessage)
        {
            UpdateLogText();
        }

        [RelayCommand]
        private void ClearLogs()
        {
            try
            {
                LogManager.ClearLogs();
                UpdateLogText();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "清空日志时出错");
            }
        }

        private void UpdateLogText()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                LogText = LogManager.GetLogText();
                return;
            }

            dispatcher.BeginInvoke(() =>
            {
                LogText = LogManager.GetLogText();
            });
        }
    }
}
