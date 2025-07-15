using Microsoft.Win32;
using Serilog;

namespace Legend2Tool.WPF.Services
{
    public class DialogService : IDialogService
    {
        private readonly ILogger _logger;

        public DialogService(ILogger logger)
        {
            _logger = logger;
        }
        public string? ShowFolderBrowserDialog(string initialPath = null!)
        {
            OpenFolderDialog dialog = new();

            // 配置打开文件夹对话框
            dialog.Multiselect = false;
            dialog.Title = "选择文件夹";

            // 弹出打开文件夹对话框
            bool? result = dialog.ShowDialog();

            // 处理打开文件夹对话框结果
            if (result == true)
            {
                // 获取已选择的文件夹
                string fullPathToFolder = dialog.FolderName;
                string folderNameOnly = dialog.SafeFolderName;

                return fullPathToFolder;
            }
            return null;
        }
    }
}
