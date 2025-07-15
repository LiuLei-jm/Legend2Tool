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
    }
}
