using Legend2Tool.WPF.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.Launcher
{
    public class LauncherConfigGEE : LauncherConfigBase
    {
        [IniConfig("Setup", "快捷方式图标")]
        public string? LauncherIcon { get; set; }
        [IniConfig("Setup", "游戏光标")]
        public string? GameCursor { get; set; }
        [IniConfig("Setup", "镶嵌光标")]
        public string? InlayCursor { get; set; }
        [IniConfig("Setup", "拆卸光标")]
        public string? DisassembleCursor { get; set; }

        [IniConfig("Setup", "Resources目录")]
        public override string? ResourcesDir { get; set; }
        [IniConfig("Setup", "主TCP列表服务器")]
        public override string? MainTcpListServer { get; set; }
        [IniConfig("Setup", "备用TCP列表服务器")]
        public override string? BackupTcpListServer { get; set; }
        [IniConfig("Setup", "主TCP端口")]
        public override int MainTcpPort { get; set; }
        [IniConfig("Setup", "备用TCP端口")]
        public override int BackupTcpPort { get; set; }
        [IniConfig("Setup", "主TCP配置文件")]
        public override string? MainTcpConfigFile { get; set; }
        [IniConfig("Setup", "备用TCP配置文件")]
        public override string? BackupTcpConfigFile { get; set; }
    }
}
