using System.Text;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using Serilog;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public class DynamicMonsterSpawningServiceTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

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
    public void ParseMapNames_ValidEntries_ReturnsNamesForPrimaryAndAliasCodes()
    {
        string[] lines =
        [
            ";comment",
            "[MAP01|MAP01_1 比奇省] DAY",
            "[FB02 地牢] FB",
            "invalid"
        ];

        IReadOnlyDictionary<string, string> names =
            DynamicMonsterSpawningService.ParseMapNames(lines);

        Assert.Equal("比奇省", names["MAP01"]);
        Assert.Equal("比奇省", names["map01_1"]);
        Assert.Equal("地牢-副本", names["FB02"]);
    }

    [Fact]
    public void CreateGenerationResults_OnlyReturnsMapsAboveLimit_WithOriginalTotals()
    {
        Dictionary<string, int> totals = new()
        {
            ["MAP01"] = 250,
            ["MAP02"] = 200,
            ["MAP03"] = 400
        };
        Dictionary<string, string> names = new()
        {
            ["MAP01"] = "比奇省"
        };

        IReadOnlyList<DynamicMonsterSpawningResult> results =
            DynamicMonsterSpawningService.CreateGenerationResults(totals, names, 200);

        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("MAP03", result.MapName);
                Assert.Equal(400, result.MonsterCount);
            },
            result =>
            {
                Assert.Equal("比奇省", result.MapName);
                Assert.Equal(250, result.MonsterCount);
            }
        );
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

    [Fact]
    public async Task GenerateRefreshMonScriptAsync_LoadGen_PreservesStructureAndRestoresFiles()
    {
        string serverDirectory = Path.Combine(
            Path.GetTempPath(),
            $"Legend2Tool-DynamicMon-{Guid.NewGuid():N}"
        );
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        string envirDirectory = Path.Combine(serverDirectory, "Mir200", "Envir");
        string mongenDirectory = Path.Combine(envirDirectory, "Mongen");
        string robotDirectory = Path.Combine(envirDirectory, "Robot_def");
        string questDirectory = Path.Combine(envirDirectory, "QuestDiary");
        string mainPath = Path.Combine(envirDirectory, "MonGen.txt");
        string referencedPath = Path.Combine(mongenDirectory, "魔龙.txt");
        string robotManagePath = Path.Combine(robotDirectory, "RobotManage.txt");
        string autoRunRobotPath = Path.Combine(robotDirectory, "AutoRunRobot.txt");
        byte[] originalMain = gb18030.GetBytes(
            ";主文件注释\r\nloadgen\t魔龙.txt\r\n"
        );
        byte[] originalReferenced = gb18030.GetBytes(
            ";地图名：魔龙\r\n61\t51\t32\t魔龙刀兵\t100\t50\t65\r\n"
        );

        try
        {
            Directory.CreateDirectory(mongenDirectory);
            Directory.CreateDirectory(robotDirectory);
            Directory.CreateDirectory(questDirectory);
            await File.WriteAllBytesAsync(mainPath, originalMain);
            await File.WriteAllBytesAsync(referencedPath, originalReferenced);
            await File.WriteAllTextAsync(robotManagePath, string.Empty, gb18030);
            await File.WriteAllTextAsync(autoRunRobotPath, string.Empty, gb18030);

            LoadedServerConfig loadedConfig = new(
                serverDirectory,
                EngineType.GEE,
                new M2ConfigBase(),
                new LauncherConfigBase(),
                new Setup(),
                []
            );
            var configStore = new ConfigStore(
                new StubConfigService(loadedConfig),
                Logger
            );
            configStore.Receive(new ServerDirectoryChangedMessage(serverDirectory));
            var service = new DynamicMonsterSpawningService(
                configStore,
                new EncodingService()
            );
            var options = new RefreshOptimizationOptions
            {
                RefreshMonTrigger = "TestRefresh",
                ClearMonTrigger = "TestClear",
                SelectedTimeUnit = "分",
                RefreshMonInterval = 2,
                ClearMonInterval = 15,
                RefreshMonMultiplier = 1,
                IsCommentMongen = true,
                MaxRefreshInterval = 30,
                MaxRefreshCount = 50,
                MaxMonstersPerMap = 200
            };

            await service.GenerateRefreshMonScriptAsync(options);

            string generatedMain = await File.ReadAllTextAsync(mainPath, gb18030);
            string generatedReferenced = await File.ReadAllTextAsync(
                referencedPath,
                gb18030
            );
            string refreshScript = await File.ReadAllTextAsync(
                Path.Combine(questDirectory, "智能刷怪.txt"),
                gb18030
            );
            Assert.Contains("loadgen\t魔龙.txt", generatedMain);
            Assert.DoesNotContain("魔龙刀兵", generatedMain);
            Assert.Contains(";61\t51\t32\t魔龙刀兵\t100\t50\t65", generatedReferenced);
            Assert.Contains("魔龙刀兵", refreshScript);

            string backupRoot = Path.Combine(
                serverDirectory,
                "Legend2ToolBackups",
                "Mongen"
            );
            string backupDirectory = Assert.Single(Directory.GetDirectories(backupRoot));
            Assert.Equal(
                originalMain,
                await File.ReadAllBytesAsync(
                    Path.Combine(backupDirectory, "Mir200", "Envir", "MonGen.txt")
                )
            );
            Assert.Equal(
                originalReferenced,
                await File.ReadAllBytesAsync(
                    Path.Combine(
                        backupDirectory,
                        "Mir200",
                        "Envir",
                        "Mongen",
                        "魔龙.txt"
                    )
                )
            );

            options.IsCommentMongen = false;
            await service.ClearRefreshMonScriptAsync(options);

            Assert.Equal(originalMain, await File.ReadAllBytesAsync(mainPath));
            Assert.Equal(originalReferenced, await File.ReadAllBytesAsync(referencedPath));
            Assert.True(File.Exists(Path.Combine(backupDirectory, "restore.completed")));
        }
        finally
        {
            if (Directory.Exists(serverDirectory))
                Directory.Delete(serverDirectory, recursive: true);
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

    private sealed class StubConfigService : IConfigService
    {
        private readonly LoadedServerConfig _loadedConfig;

        public StubConfigService(LoadedServerConfig loadedConfig)
        {
            _loadedConfig = loadedConfig;
        }

        public EngineType CheckEngineType(string serverDirectory) => _loadedConfig.EngineType;
        public Task<string> GetExternalIpAddressAsync() => throw new NotSupportedException();
        public bool CheckPorts(int[] portsToCheck) => throw new NotSupportedException();
        public string GetResourcesDirByGamePinyin(string launcherName) =>
            throw new NotSupportedException();
        public string GetLauncherName(ConfigStore configStore) => throw new NotSupportedException();
        public Task SaveConfigFileAsync(ConfigStore configStore) =>
            throw new NotSupportedException();
        public LoadedServerConfig LoadServerConfig(string serverDirectory) => _loadedConfig;
        public void ApplyDefaultAuxiliarySettings(ConfigStore configStore) =>
            throw new NotSupportedException();
        public Task GenerateCleanupScriptAsync(string baseDirectory) =>
            throw new NotSupportedException();
    }
}
