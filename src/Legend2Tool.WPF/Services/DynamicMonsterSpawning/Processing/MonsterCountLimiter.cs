using System.IO;

namespace Legend2Tool.WPF.Services.DynamicMonsterSpawning.Processing;

internal sealed class MonsterCountLimiter
{
    public void Limit(Dictionary<string, List<string>> mapMonsters,
        Dictionary<string, int> mapMonsterCounts, int maxMonstersPerMap)
    {
        foreach (string mapCode in mapMonsters.Keys.ToList())
        {
            int total = mapMonsterCounts[mapCode];
            if (total <= maxMonstersPerMap)
                continue;

            decimal scale = (maxMonstersPerMap / (decimal)total * 100m) / 100m;
            List<string> adjustedScripts = [];
            foreach (string script in mapMonsters[mapCode])
            {
                string[] parts = script.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 6 || !int.TryParse(parts[6], out int originalCount))
                    throw new InvalidDataException($"无法读取地图 {mapCode} 的 MongenEX 怪物数量：{script}");

                int adjustedCount = decimal.ToInt32(decimal.Floor(originalCount * scale));
                if (adjustedCount < 1)
                    continue;
                parts[6] = adjustedCount.ToString();
                adjustedScripts.Add(string.Join(' ', parts));
            }
            mapMonsters[mapCode] = adjustedScripts;
        }
    }
}
