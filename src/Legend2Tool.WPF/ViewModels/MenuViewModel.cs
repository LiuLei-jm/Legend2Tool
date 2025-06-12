using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Legend2Tool.WPF.Services;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class MenuViewModel : ObservableRecipient
    {
        private readonly IDialogService _dialogService;

        public MenuViewModel(IDialogService dialogService)
        {
            IsActive = true; 
            _dialogService = dialogService;
        }

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        string _serverDirectory = string.Empty;

        partial void OnServerDirectoryChanged(string value)
        {
            WeakReferenceMessenger.Default.Send(new ValueChangedMessage<string>(value));
        }

        [RelayCommand]
        void SetServerDirectory()
        {
            // 使用对话框服务显示目录选择对话框
            // 将当前的 SelectedDirectoryPath 作为初始路径传入
            string initialPath = string.IsNullOrEmpty(ServerDirectory) ? string.Empty : ServerDirectory;
            string? selectedPath = _dialogService.ShowFolderBrowserDialog(initialPath);

            // 如果用户选择了目录 (即返回值不为 null)
            if (selectedPath != null)
            {
                // 更新 ViewModel 中的属性
                ServerDirectory = selectedPath;
            }
        }
    }
}
