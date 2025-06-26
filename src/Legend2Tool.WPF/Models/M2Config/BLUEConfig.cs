using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config.M2Config
{
    public class BLUEConfig : M2ConfigBase
    {
        [IniConfig("GameConf","DataTableFile")]
        public string? DataTableFile { get; set; }
    }
}
