using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config
{
    public class M2ConfigBase
    {
        [IniConfig("GameConf", "GameDirectory")]
        public string? GameDirectory { get; set; }
        [IniConfig("GameConf", "HeroDBName")]
        public string? HeroDBName { get; set; }
        [IniConfig("GameConf", "GameName")]
        public string? GameName { get; set; }
        [IniConfig("GameConf", "ExtIPaddr")]
        public string? ExtIPaddr { get; set; }
        [IniConfig("GameConf", "DynamicIPMode")]
        public int DynamicIPMode { get; set; }
        [IniConfig("DBServer", "GatePort")]
        public int DBServerGatePort { get; set; }
        [IniConfig("DBServer", "ServerPort")]
        public int DBServerServerPort { get; set; }
        [IniConfig("DBServer", "GetStart")]
        public int DBServerGetStart { get; set; }
        [IniConfig("M2Server", "GatePort")]
        public int M2ServerGatePort { get; set; }
        [IniConfig("M2Server", "MsgSrvPort")]
        public int M2ServerMsgSrvPort { get; set; }
        [IniConfig("M2Server", "GetStart")]
        public int M2ServerGetStart { get; set; }
        [IniConfig("RunGate", "GatePort1")]
        public int RunGateGatePort1 { get; set; }
        [IniConfig("RunGate", "GatePort2")]
        public int RunGateGatePort2 { get; set; }
        [IniConfig("RunGate", "GatePort3")]
        public int RunGateGatePort3 { get; set; }
        [IniConfig("RunGate", "GatePort4")]
        public int RunGateGatePort4 { get; set; }
        [IniConfig("RunGate", "GatePort5")]
        public int RunGateGatePort5 { get; set; }
        [IniConfig("RunGate", "GatePort6")]
        public int RunGateGatePort6 { get; set; }
        [IniConfig("RunGate", "GatePort7")]
        public int RunGateGatePort7 { get; set; }
        [IniConfig("RunGate", "GatePort8")]
        public int RunGateGatePort8 { get; set; }
        [IniConfig("RunGate", "Count")]
        public int RunGateCount { get; set; }
        [IniConfig("LoginGate", "GetStart")]
        public int LoginGateGetStart { get; set; }
        [IniConfig("LoginGate", "GatePort")]
        public int LoginGateGatePort { get; set; }
        [IniConfig("SelGate", "GatePort")]
        public int SelGateGatePort { get; set; }
        [IniConfig("SelGate", "GetStart")]
        public int SelGateGetStart { get; set; }

        [IniConfig("SelGate", "GatePort1")]
        public int SelGateGatePort1 { get; set; }
        [IniConfig("SelGate", "GetStart1")]
        public int SelGateGetStart1 { get; set; }
        [IniConfig("LoginServer", "GatePort")]
        public int LoginServerGatePort { get; set; }
        [IniConfig("LoginServer", "ServerPort")]
        public int LoginServerServerPort { get; set; }
        [IniConfig("LoginServer", "GetStart")]
        public int LoginServerGetStart { get; set; }
        [IniConfig("LogServer", "Port")]
        public int LogServerPort { get; set; }
        [IniConfig("LogServer", "GetStart")]
        public int LogServerGetStart { get; set; }

    }
}
