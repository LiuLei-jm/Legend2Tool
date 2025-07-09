using Legend2Tool.WPF.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.BackList
{
    public class GEEBackList : BackListBase
    {
        [IniConfig("", "IsCompress")]
        public int IsCompress { get; set; } = 0;
    }
}
