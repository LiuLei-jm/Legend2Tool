using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services.Infrastructure.Files;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ScriptOptimization;
using Legend2Tool.WPF.Services.ScriptOptimization.Modular;
using Legend2Tool.WPF.Services.ServerConfiguration;
using Legend2Tool.WPF.State;
using Microsoft.Data.Sqlite;
using Serilog;
using System.Text;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class DropRateGenerationTests
{
    [Theory]
    [InlineData(EngineType.GEE, "site")]
    [InlineData(EngineType.BLUE, "site")]
    [InlineData(EngineType.HGE, "site")]
    [InlineData(EngineType.GEE, "none")]
    [InlineData(EngineType.GEE, "missing")]
    public async Task Generate_UsesSelectedSiteOrOriginalOutputAndKeepsMapDesc(EngineType engine, string destination)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        SQLitePCL.Batteries_V2.Init();
        string root = Path.Combine(Path.GetTempPath(), $"Legend2Tool-Generate-{Guid.NewGuid():N}");
        string server = Path.Combine(root, "server");
        string site = Path.Combine(root, "site");
        string envir = Path.Combine(server, "Mir200", "Envir");
        Directory.CreateDirectory(envir);
        foreach (string directory in new[] { "MapQuest_Def", "Market_Def", "QuestDiary", "Robot_Def", "MonItems" })
            Directory.CreateDirectory(Path.Combine(envir, directory));
        Directory.CreateDirectory(Path.Combine(server, "Mud2"));
        string dbPath = Path.Combine(server, "Mud2", "test.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE StdItems (Idx INTEGER, Name TEXT, StdMode INTEGER);
                    CREATE TABLE item (ClassID INTEGER, Name TEXT, StdMode INTEGER);
                    CREATE TABLE Monster (Name TEXT);
                    INSERT INTO StdItems VALUES (1, '测试装备', 10);
                    INSERT INTO item VALUES (1, '测试装备', 10);
                    INSERT INTO Monster VALUES ('测试怪物');
                    """;
                command.ExecuteNonQuery();
            }
            foreach (string file in new[] { "MapInfo.txt", "MerChant.txt", "Mongen.txt", "MapEvent.txt" })
                File.WriteAllText(Path.Combine(envir, file), string.Empty);
            Directory.CreateDirectory(Path.Combine(site, "js"));
            string customPath = Path.Combine(site, "js", "custom.js");
            if (destination == "site") File.WriteAllText(customPath, "var version_list = [];\nvar keep = true;");
            M2ConfigBase engineConfig = engine switch
            {
                EngineType.BLUE => new BLUEConfig { DataTableFile = dbPath },
                EngineType.HGE => new HGEConfig { SQLiteName = dbPath },
                _ => new GEEConfig { SqliteDBName = dbPath }
            };
            var config = new LoadedServerConfig(server, engine, engineConfig,
                new LauncherConfigBase { LauncherName = "测试版本", ResourcesDir = "test-version" }, new(), []);
            using var logger = new LoggerConfiguration().CreateLogger();
            var store = new ConfigStore(new StubConfigService(config), logger);
            store.Receive(new ServerDirectoryChangedMessage(server));
            var encoding = new EncodingService();
            var siteService = new DropRateSiteService(encoding);
            var service = new ModularScriptOptimizationService(store, encoding, new FileService(), new ProgressStore(), logger, siteService);

            await service.DropRateCalculatorAsync(destination == "none" ? null : site);

            string originalDirectory = Path.Combine(server, "测试版本");
            Assert.True(File.Exists(Path.Combine(originalDirectory, "MapDesc1.dat")));
            if (destination == "site")
            {
                var version = Assert.Single(siteService.Load(site));
                Assert.Equal("测试版本", version.Name);
                Assert.Equal("test-version", version.Data);
                string data = File.ReadAllText(Path.Combine(site, "data", "test-version.js"));
                Assert.Contains("测试装备", data);
                Assert.Contains("测试怪物", data);
                Assert.EndsWith("var keep = true;", File.ReadAllText(customPath));
                Assert.False(File.Exists(Path.Combine(originalDirectory, "custom.js")));
                Assert.False(File.Exists(Path.Combine(originalDirectory, "test-version.js")));
            }
            else
            {
                Assert.Contains("name: \"测试版本\"", File.ReadAllText(Path.Combine(originalDirectory, "custom.js")));
                Assert.Contains("测试装备", File.ReadAllText(Path.Combine(originalDirectory, "test-version.js")));
                Assert.False(Directory.Exists(Path.Combine(site, "data")));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubConfigService(LoadedServerConfig config) : IConfigService
    {
        public LoadedServerConfig LoadServerConfig(string serverDirectory) => config;
        public EngineType CheckEngineType(string serverDirectory) => config.EngineType;
        public Task<string> GetExternalIpAddressAsync() => throw new NotSupportedException();
        public bool CheckPorts(int[] portsToCheck) => throw new NotSupportedException();
        public string GetResourcesDirByGamePinyin(string launcherName) => throw new NotSupportedException();
        public string GetLauncherName(ConfigStore configStore) => throw new NotSupportedException();
        public Task SaveConfigFileAsync(ConfigStore configStore) => throw new NotSupportedException();
        public void ApplyDefaultAuxiliarySettings(ConfigStore configStore) => throw new NotSupportedException();
        public Task GenerateCleanupScriptAsync(string baseDirectory) => throw new NotSupportedException();
    }
}
