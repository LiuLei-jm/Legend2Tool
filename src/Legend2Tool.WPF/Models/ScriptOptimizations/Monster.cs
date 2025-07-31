using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class Monster
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<string> Std { get; set; } = ["-1"];
        public List<string> Map { get; set; } = ["-1"];
        public List<string> Npc { get; set; } = ["-1"];
        public List<string> Bot { get; set; } = ["-1"];
    }
}
