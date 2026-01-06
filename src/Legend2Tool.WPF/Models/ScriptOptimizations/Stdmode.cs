using System.Collections.Concurrent;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class StdMode
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public ConcurrentDictionary<string, byte> Mons { get; set; } =
            new ConcurrentDictionary<string, byte>() { ["-1"] = 0 };
        public ConcurrentDictionary<string, byte> Npcs { get; set; } =
            new ConcurrentDictionary<string, byte>() { ["-1"] = 0 };
    }
}
