using System.Text;
using System.IO;
using Legend2Tool.WPF.Commons;

namespace Legend2Tool.WPF.Services.DynamicMonsterSpawning.Reading;

internal sealed class MapNameReader
{
    public IReadOnlyDictionary<string, string> Read(string path, Encoding fallback, Func<string, Encoding> detect)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            return Parse(File.ReadLines(path, detect(path)));
        }
        catch (IOException) { return new Dictionary<string, string>(); }
        catch (UnauthorizedAccessException) { return new Dictionary<string, string>(); }
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var mapNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            int closingBracketIndex = trimmedLine.IndexOf(']');
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(';')
                || !trimmedLine.StartsWith('[') || closingBracketIndex <= 1)
                continue;

            string[] mapParts = trimmedLine[1..closingBracketIndex]
                .Split(AppConstants.EmptySeparator, StringSplitOptions.RemoveEmptyEntries);
            if (mapParts.Length < 2)
                continue;

            string mapName = mapParts[1].Trim();
            if (trimmedLine.Contains("FB", StringComparison.OrdinalIgnoreCase))
                mapName = $"{mapName}-副本";

            foreach (string mapCode in mapParts[0].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mapNames.TryAdd(mapCode, mapName);
        }
        return mapNames;
    }
}
