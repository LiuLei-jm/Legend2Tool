using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class MapData
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public HashSet<string> Mons { get; set; } = ["-1"];
        public HashSet<string> Npcs { get; set; } = ["-1"];
        public List<string> Paths { get; set; } = ["没有找到"];
        public List<string> BestPaths { get; set; } = [];
        public HashSet<string> FromMapLists { get; set; } = [];
        public HashSet<string> AddedPaths { get; set; } = [];
        public bool IsMainCity ;

    }
}
