using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.Launcher
{
    public class LauncherConfigBase
    {
        [IniConfig("Setup", "快捷方式")]
        public string? LauncherName { get; set; }
        [IniConfig("Setup", "背景图片")]
        public string? BackgroundImage { get; set; }
        [IniConfig("Setup", "多开数量")]
        public int MultiInstanceCount { get; set; }
        [IniConfig("Setup", "更新密码")]
        public string? UpdatePassword { get; set; }
        [IniConfig("Setup", "配置文件")]
        public string? MainAddress { get; set; }
        [IniConfig("Setup", "备用地址")]
        public string? BackupAddress { get; set; }

        public virtual string? ResourcesDir { get; set; }
        public virtual string? MainTcpListServer { get; set; }
        public virtual string? BackupTcpListServer { get; set; }
        public virtual int MainTcpPort { get; set; }
        public virtual int BackupTcpPort { get; set; }
        public virtual string? MainTcpConfigFile { get; set; }
        public virtual string? BackupTcpConfigFile { get; set; }
    }
}
