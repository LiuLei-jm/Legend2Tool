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
        public HashSet<string> Stds { get; set; } = ["-1"];
        public HashSet<string> Maps { get; set; } = ["-1"];
        public HashSet<string> Npcs { get; set; } = ["-1"];
        public HashSet<string> Bots { get; set; } = ["-1"];
    }
}
