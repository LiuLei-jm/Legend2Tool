using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config.M2Config
{
    public class GEEConfig : M2ConfigBase
    {
        [IniConfig("GameConf", "UseSqliteDB")]
        public int UseSqliteDB { get; set; }
        [IniConfig("GameConf", "SqliteDBName")]
        public string? SqliteDBName { get; set; }
        [IniConfig("GameConf", "SqliteDBFile")]
        public string? SqliteDBFile { get; set; }
        [IniConfig("LoginGate", "GetStart1")]
        public int LoginGateGetStart1 { get; set; }
        [IniConfig("LoginGate", "GatePort1")]
        public int LoginGateGatePort1 { get; set; }
        [IniConfig("RunGate", "GetMultiThread")]
        public int RunGateGetMultiThread { get; set; }
        [IniConfig("RunGate", "DBPort1")]
        public int RunGateDBPort1 { get; set; }
        [IniConfig("RunGate", "DBPort2")]
        public int RunGateDBPort2 { get; set; }
        [IniConfig("RunGate", "DBPort3")]
        public int RunGateDBPort3 { get; set; }
        [IniConfig("RunGate", "DBPort4")]
        public int RunGateDBPort4 { get; set; }
        [IniConfig("RunGate", "DBPort5")]
        public int RunGateDBPort5 { get; set; }
        [IniConfig("RunGate", "DBPort6")]
        public int RunGateDBPort6 { get; set; }
        [IniConfig("RunGate", "DBPort7")]
        public int RunGateDBPort7 { get; set; }
        [IniConfig("RunGate", "DBPort8")]
        public int RunGateDBPort8 { get; set; }
        [IniConfig("ClearServer", "MyGetTxtNum")]
        public int MyGetTxtNum { get; set; } = 0;
        public List<string> MyGetTxtList { get; set; } = new List<string>();
        [IniConfig("ClearServer", "MyGetFileNum")]
        public int MyGetFileNum { get; set; } = 0;
        public List<string> MyGetFileList { get; set; } = new List<string>();
        [IniConfig("ClearServer", "MyGetDirNum")]
        public int MyGetDirNum { get; set; } = 0;
        public List<string> MyGetDirList { get; set; } = new List<string>();
    }
}
