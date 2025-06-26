using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config.M2Config
{
    public class GEEConfig : M2ConfigBase
    {
        [IniConfig("GameConf","UseSqliteDB")]
        public int UseSqliteDB { get; set; }
        [IniConfig("GameConf","SqliteDBName")]
        public string? SqliteDBName { get; set; }
        [IniConfig("GameConf","SqliteDBFile")]
        public string? SqliteDBFile { get; set; }
    }
}
