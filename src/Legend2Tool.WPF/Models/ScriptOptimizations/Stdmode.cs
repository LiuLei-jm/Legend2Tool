using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class StdMode
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public List<string> Mon { get; set; } = ["-1"];
        public List<string> Npc { get; set; } = ["-1"];
    }
}
