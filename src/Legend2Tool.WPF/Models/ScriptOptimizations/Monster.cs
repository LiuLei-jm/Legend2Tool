using System.Collections.Concurrent;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class Monster
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public ConcurrentDictionary<string, byte> Stds { get; set; } =
            new ConcurrentDictionary<string, byte>() { ["-1"] = 0 };
        public ConcurrentDictionary<string, byte> Maps { get; set; } =
            new ConcurrentDictionary<string, byte>() { ["-1"] = 0 };
        public ConcurrentDictionary<string, byte> Npcs { get; set; } =
            new ConcurrentDictionary<string, byte>() { ["-1"] = 0 };
        public ConcurrentDictionary<string, byte> Bots { get; set; } =
            new ConcurrentDictionary<string, byte>() { ["-1"] = 0 };
    }
}
