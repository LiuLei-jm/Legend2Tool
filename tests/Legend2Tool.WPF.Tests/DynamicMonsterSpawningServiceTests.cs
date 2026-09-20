using System.Text;
using Legend2Tool.WPF.Services;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public class DynamicMonsterSpawningServiceTests
{
    public DynamicMonsterSpawningServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void LimitMapMonsterCounts_TotalBelow200_KeepsOriginalCounts()
    {
        Dictionary<string, List<string>> scripts = CreateScripts(50, 60, 70);
        Dictionary<string, int> totals = new() { ["MAP01"] = 180 };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 200);

        Assert.Equal([50, 60, 70], ReadCounts(scripts["MAP01"]));
        Assert.Equal(180, totals["MAP01"]);
    }

    [Fact]
    public void LimitMapMonsterCounts_TotalEquals200_KeepsOriginalCounts()
    {
        Dictionary<string, List<string>> scripts = CreateScripts(50, 60, 90);
        Dictionary<string, int> totals = new() { ["MAP01"] = 200 };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 200);

        Assert.Equal([50, 60, 90], ReadCounts(scripts["MAP01"]));
        Assert.Equal(200, totals["MAP01"]);
    }

    [Fact]
    public void LimitMapMonsterCounts_TotalAbove200_ScalesEachCount()
    {
        Dictionary<string, List<string>> scripts = CreateScripts(100, 200, 100);
        Dictionary<string, int> totals = new() { ["MAP01"] = 400 };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 200);

        Assert.Equal([50, 100, 50], ReadCounts(scripts["MAP01"]));
        Assert.Equal(400, totals["MAP01"]);
    }

    [Fact]
    public void LimitMapMonsterCounts_FractionalCounts_RoundsDown()
    {
        Dictionary<string, List<string>> scripts = CreateScripts(100, 100, 100);
        Dictionary<string, int> totals = new() { ["MAP01"] = 300 };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 200);

        Assert.Equal([66, 66, 66], ReadCounts(scripts["MAP01"]));
        Assert.Equal(300, totals["MAP01"]);
    }

    [Fact]
    public void LimitMapMonsterCounts_AdjustedZero_RemovesCommand()
    {
        Dictionary<string, List<string>> scripts = CreateScripts(1, 200);
        Dictionary<string, int> totals = new() { ["MAP01"] = 201 };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 200);

        Assert.Equal([199], ReadCounts(scripts["MAP01"]));
        Assert.Equal(201, totals["MAP01"]);
    }

    [Fact]
    public void LimitMapMonsterCounts_MultipleMaps_OnlyScalesMapAboveLimit()
    {
        Dictionary<string, List<string>> scripts = new()
        {
            ["MAP01"] = CreateScripts(40, 60)["MAP01"],
            ["MAP02"] = CreateScripts(150, 150)["MAP01"]
        };
        Dictionary<string, int> totals = new()
        {
            ["MAP01"] = 100,
            ["MAP02"] = 300
        };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 200);

        Assert.Equal([40, 60], ReadCounts(scripts["MAP01"]));
        Assert.Equal(100, totals["MAP01"]);
        Assert.Equal([100, 100], ReadCounts(scripts["MAP02"]));
        Assert.Equal(300, totals["MAP02"]);
    }

    [Fact]
    public void LimitMapMonsterCounts_ConfiguredLimit_UsesConfiguredValue()
    {
        Dictionary<string, List<string>> scripts = CreateScripts(100, 100);
        Dictionary<string, int> totals = new() { ["MAP01"] = 200 };

        DynamicMonsterSpawningService.LimitMapMonsterCounts(scripts, totals, 100);

        Assert.Equal([50, 50], ReadCounts(scripts["MAP01"]));
    }

    [Fact]
    public void ResolveEncodingForWrite_KnownEncoding_PreservesDetectedEncoding()
    {
        Encoding fallback = Encoding.GetEncoding("GB18030");
        var detection = new EncodingDetectionResult(
            new UTF8Encoding(false),
            "严格 UTF-8 校验通过"
        );

        Encoding result = DynamicMonsterSpawningService.ResolveEncodingForWrite(
            detection,
            fallback
        );

        Assert.Equal(Encoding.UTF8.CodePage, result.CodePage);
    }

    [Fact]
    public async Task ResolveEncodingForWrite_AsciiFile_WritesChineseAsGb18030()
    {
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"Legend2Tool-DynamicMon-{Guid.NewGuid():N}.txt"
        );

        try
        {
            await File.WriteAllTextAsync(path, "#AutoRun NPC MIN 2 Trigger", Encoding.ASCII);
            EncodingDetectionResult detection = new EncodingService()
                .DetectFileEncodingResult(path);

            Encoding writeEncoding = DynamicMonsterSpawningService.ResolveEncodingForWrite(
                detection,
                gb18030
            );
            await File.WriteAllTextAsync(path, "智能刷怪", writeEncoding);

            Assert.False(detection.IsKnown);
            Assert.Equal(gb18030.GetBytes("智能刷怪"), await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Dictionary<string, List<string>> CreateScripts(params int[] counts) =>
        new()
        {
            ["MAP01"] = counts
                .Select((count, index) =>
                    $"MonGenEX MAP01 {100 + index} {100 + index} Monster{index} 20 {count} 0 255"
                )
                .ToList()
        };

    private static int[] ReadCounts(IEnumerable<string> scripts) => scripts
        .Select(script => int.Parse(script.Split(' ')[6]))
        .ToArray();
}
