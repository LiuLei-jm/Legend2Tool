using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.M2Config;

public class HGEConfig : M2ConfigBase
{
    // GameConf section
    [IniConfig("GameConf", "SQLiteName")]
    public string SQLiteName { get; set; } = string.Empty;
    [IniConfig("GameConf", "UseSQLite")]
    public int UseSQLite { get; set; }

    // Bak section
    [IniConfig("Bak", "DataDir1")]
    public string DataDir1 { get; set; } = string.Empty;
    [IniConfig("Bak", "BakDir1")]
    public string BakDir1 { get; set; } = string.Empty;
    [IniConfig("Bak", "TimeCls1")]
    public int TimeCls1 { get; set; }
    [IniConfig("Bak", "Hour1")]
    public int Hour1 { get; set; }
    [IniConfig("Bak", "Minute1")]
    public int Minute1 { get; set; }
    [IniConfig("Bak", "OnlyBakDatabase1")]
    public int OnlyBakDatabase1 { get; set; }
    [IniConfig("Bak", "Count")]
    public int Count { get; set; }
    [IniConfig("Bak", "BakAuto")]
    public int BakAuto { get; set; }
    [IniConfig("Bak", "BakReduce")]
    public int BakReduce { get; set; }
}
