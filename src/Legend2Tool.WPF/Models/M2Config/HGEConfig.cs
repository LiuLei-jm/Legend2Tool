using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config;

public class HGEConfig : M2ConfigBase
{
    [IniConfig("GameConf", "SQLiteName")]
    public string SQLiteName { get; set; } = string.Empty;

    [IniConfig("GameConf", "UseSQLite")]
    public int UseSQLite { get; set; }
}
