namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public sealed class DynamicMonsterSpawningResult
    {
        public string MapCode { get; init; } = string.Empty;
        public string MapName { get; init; } = string.Empty;
        public int MonsterCount { get; init; }
    }
}
