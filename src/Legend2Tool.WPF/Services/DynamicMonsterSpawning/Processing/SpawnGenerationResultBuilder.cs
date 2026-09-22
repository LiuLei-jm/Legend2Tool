using Legend2Tool.WPF.Models.ScriptOptimizations;

namespace Legend2Tool.WPF.Services.DynamicMonsterSpawning.Processing;

internal sealed class SpawnGenerationResultBuilder
{
    public IReadOnlyList<DynamicMonsterSpawningResult> Build(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyDictionary<string, string> names,
        int maxMonstersPerMap) => counts
            .Where(entry => entry.Value > maxMonstersPerMap)
            .Select(entry => new DynamicMonsterSpawningResult
            {
                MapCode = entry.Key,
                MapName = names.GetValueOrDefault(entry.Key, entry.Key),
                MonsterCount = entry.Value
            })
            .OrderByDescending(result => result.MonsterCount)
            .ThenBy(result => result.MapName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
