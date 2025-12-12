using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Legend2Tool.WPF.Services;
using Serilog;

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
            // Update the LogText property to trigger UI update
            LogText = LogManager.GetLogText();
        }

        [RelayCommand]
        private void ClearLogs()
        {
            try
            {
                LogManager.ClearLogs();
                LogText = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "清空日志时出错");
            }
        }
    }
}