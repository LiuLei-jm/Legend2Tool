using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config.M2Config
{
    public class GOMConfig : M2ConfigBase
    {
        [IniConfig("GameConf","UseAccessDB")]
        public int UseAccessDB { get; set; }
        [IniConfig("GameConf","AccessFileName")]
        public string? AccessFileName { get; set; }

        [IniConfig("SelGate","GatePort")]
        public int SelGateGatePort { get; set; }
        [IniConfig("SelGate","GatePort1")]
        public int SelGateGatePort1 { get; set; }
    }
}
