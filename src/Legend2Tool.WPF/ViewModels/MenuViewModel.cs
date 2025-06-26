using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Services;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class MenuViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;

        public MenuViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        [ObservableProperty]
        string _serverDirectory = string.Empty;

        partial void OnServerDirectoryChanged(string value)
        {
            WeakReferenceMessenger.Default.Send(new ServerDirectoryChangedMessage(value));
        }

        [ObservableProperty]
        string _patchDirectory = string.Empty;

        partial void OnPatchDirectoryChanged(string value)
        {
            WeakReferenceMessenger.Default.Send(new PatchDirectoryChanedMessage(value));
        }

        [RelayCommand]
        void SetDirectory(string directory)
        {
            // 使用对话框服务显示目录选择对话框
            // 将当前的 SelectedDirectoryPath 作为初始路径传入
            string initialPath = string.IsNullOrEmpty(ServerDirectory)
                ? string.Empty
                : ServerDirectory;
            string? selectedPath = _dialogService.ShowFolderBrowserDialog(initialPath);

            // 如果用户选择了目录 (即返回值不为 null)
            if (selectedPath != null)
            {
                // 更新 ViewModel 中的属性
                //ServerDirectory = selectedPath;
                switch (directory)
                {
                    case "server":
                        ServerDirectory = selectedPath;
                        break;
                    case "patch":
                        PatchDirectory = selectedPath;
                        break;
                    default:
                        throw new ArgumentException($"目录类型无效: {directory}");
                }
            }
        }
    }
}
