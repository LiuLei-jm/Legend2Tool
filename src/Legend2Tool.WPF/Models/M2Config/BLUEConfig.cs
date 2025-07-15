using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config.M2Config
{
    public class BLUEConfig : M2ConfigBase
    {
        [IniConfig("GameConf", "DataTableFile")]
        public string? DataTableFile { get; set; }

        [IniConfig("LoginServer", "MonPort")]
        public int LoginServerMonPort { get; set; }
        [IniConfig("Backup", "backup")]
        public int Backup { get; set; } = 0;
        [IniConfig("Backup", "mode")]
        public int Mode { get; set; } = 0;
        [IniConfig("Backup", "interval")]
        public int Interval { get; set; } = 0;
        [IniConfig("Backup", "attime")]
        public string Attime { get; set; } = string.Empty;
        [IniConfig("Backup", "数据备份目录")]
        public int 数据备份目录 { get; set; } = 0;
        [IniConfig("Backup", "数据备份目录_path")]
        public string 数据备份目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "WinRAR目录")]
        public int WinRAR目录 { get; set; } = 0;
        [IniConfig("Backup", "WinRAR目录_path")]
        public string WinRAR目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "FDB目录")]
        public int FDB目录 { get; set; } = 0;
        [IniConfig("Backup", "FDB目录_path")]
        public string FDB目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "IDDB目录")]
        public int IDDB目录 { get; set; } = 0;
        [IniConfig("Backup", "IDDB目录_path")]
        public string IDDB目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "行会目录")]
        public int 行会目录 { get; set; } = 0;
        [IniConfig("Backup", "行会目录_path")]
        public string 行会目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "沙城目录")]
        public int 沙城目录 { get; set; } = 0;
        [IniConfig("Backup", "沙城目录_path")]
        public string 沙城目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "脚本数据目录")]
        public int 脚本数据目录 { get; set; } = 0;
        [IniConfig("Backup", "脚本数据目录_path")]
        public string 脚本数据目录_path { get; set; } = string.Empty;
        [IniConfig("Backup", "自定义目录1")]
        public int 自定义目录1 { get; set; } = 0;
        [IniConfig("Backup", "自定义目录1_path")]
        public string 自定义目录1_path { get; set; } = string.Empty;
        [IniConfig("Backup", "自定义目录2")]
        public int 自定义目录2 { get; set; } = 0;
        [IniConfig("Backup", "自定义目录2_path")]
        public string 自定义目录2_path { get; set; } = string.Empty;
    }
}
