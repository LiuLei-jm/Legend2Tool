using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Services;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class MenuViewModel : ViewModelBase, IRecipient<PatchDirectoryChangedMessage>
    {
        private readonly IDialogService _dialogService;

        public MenuViewModel(IDialogService dialogService)
        {
            WeakReferenceMessenger.Default.Register<PatchDirectoryChangedMessage>(this);
            _dialogService = dialogService;
        }

        [ObservableProperty]
        string _serverDirectory = string.Empty;


        [ObservableProperty]
        string _patchDirectory = string.Empty;


        [RelayCommand]
        void SetServerDirectory()
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
                ServerDirectory = selectedPath;
                WeakReferenceMessenger.Default.Send(new ServerDirectoryChangedMessage(ServerDirectory));
            }
        }
        [RelayCommand]
        void SetPatchDirectory()
        {
            // 使用对话框服务显示目录选择对话框
            // 将当前的 SelectedDirectoryPath 作为初始路径传入
            string initialPath = string.IsNullOrEmpty(PatchDirectory)
                ? string.Empty
                : PatchDirectory;
            string? selectedPath = _dialogService.ShowFolderBrowserDialog(initialPath);
            // 如果用户选择了目录 (即返回值不为 null)
            if (selectedPath != null)
            {
                // 更新 ViewModel 中的属性
                PatchDirectory = selectedPath;
                WeakReferenceMessenger.Default.Send(new PatchDirectoryChangedMessage(PatchDirectory));
            }
        }

        public void Receive(PatchDirectoryChangedMessage message)
        {
            if (!string.Equals(PatchDirectory, message.Value, StringComparison.OrdinalIgnoreCase))
            {
                PatchDirectory = message.Value;
            }
        }
    }
}
