using Microsoft.Win32;
using Serilog;
using System.IO;

namespace Legend2Tool.WPF.Services.Presentation
{
    public class DialogService : IDialogService
    {
        public DialogService(ILogger logger)
        {
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

        public string? ShowFileBrowserDialog(
            string initialPath = null!,
            string filter = "所有文件|*.*"
        )
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = filter,
                Multiselect = false,
                Title = "选择数据库文件"
            };

            if (File.Exists(initialPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(initialPath);
                dialog.FileName = Path.GetFileName(initialPath);
            }
            else if (Directory.Exists(initialPath))
            {
                dialog.InitialDirectory = initialPath;
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
