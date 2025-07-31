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
        public List<string> Mon { get; set; } = ["-1"];
        public List<string> Npc { get; set; } = ["-1"];
        public List<string> Path { get; set; } = ["没有找到"];
        public List<string> BestPath { get; set; } = [];
        public HashSet<string> FromMapList { get; set; } = [];
        public HashSet<string> AddedPath { get; set; } = [];
        public bool IsMainCity ;

    }
}
