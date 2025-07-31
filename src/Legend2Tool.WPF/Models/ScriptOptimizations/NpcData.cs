using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class NpcData
    {
        public int Id { get; set; }
        public string? FilePath { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public string? Mname { get; set; }
        public string? Mxy { get; set; }
        public List<string> Give { get; set; } = ["没有"];
        public List<string> Take { get; set; } = ["没有"];
        public List<string> Move { get; set; } = ["没有"];
    }
}
