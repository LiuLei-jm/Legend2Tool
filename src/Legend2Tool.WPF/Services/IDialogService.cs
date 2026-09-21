namespace Legend2Tool.WPF.Services
{
    public interface IDialogService
    {
        /// <summary>
        /// 显示一个目录选择对话框。
        /// </summary>
        /// <param name="initialPath">对话框打开时默认选中的路径 (可选)。</param>
        /// <returns>用户选择的目录路径，如果取消则返回 null。</returns>
        string? ShowFolderBrowserDialog(string initialPath = null!);

        /// <summary>
        /// 显示一个文件选择对话框。
        /// </summary>
        /// <param name="initialPath">初始文件或目录路径。</param>
        /// <param name="filter">文件类型筛选器。</param>
        /// <returns>用户选择的文件路径，如果取消则返回 null。</returns>
        string? ShowFileBrowserDialog(
            string initialPath = null!,
            string filter = "所有文件|*.*"
        );
    }
}
