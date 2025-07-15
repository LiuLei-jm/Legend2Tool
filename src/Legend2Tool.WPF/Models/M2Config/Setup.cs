using Legend2Tool.WPF.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.M2Config
{
    public class Setup
    {
        [IniConfig("Share", "ChatDir")]
        public string ChatDir { get; set; } = string.Empty;
        [IniConfig("Share", "BoxsDir")]
        public string BoxsDir { get; set; } = string.Empty;
        [IniConfig("Share", "BoxsFile")]
        public string BoxsFile { get; set; } = string.Empty;
        [IniConfig("Share", "SortDir")]
        public string SortDir { get; set; } = string.Empty;
    }
}
