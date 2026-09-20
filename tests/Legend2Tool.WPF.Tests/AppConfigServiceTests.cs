using Legend2Tool.WPF.Services;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class AppConfigServiceTests
{
    [Fact]
    public void LoadOrCreate_MissingFile_CreatesConfigWithDefaults()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "config.json");

        try
        {
            var config = new AppConfigService(path).LoadOrCreate();

            Assert.True(File.Exists(path));
            Assert.Equal(50, config.DynamicMonsterSpawning.MaxRefreshCount);
            Assert.Equal(200, config.DynamicMonsterSpawning.MaxMonstersPerMap);
            Assert.Contains("DynamicMonsterSpawning", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_UsesConfiguredValues()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "config.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "DynamicMonsterSpawning": {
                    "RefreshMonInterval": 8,
                    "MaxRefreshCount": 35,
                    "MaxMonstersPerMap": 120
                  }
                }
                """
            );

            var config = new AppConfigService(path).LoadOrCreate();

            Assert.Equal(8, config.DynamicMonsterSpawning.RefreshMonInterval);
            Assert.Equal(35, config.DynamicMonsterSpawning.MaxRefreshCount);
            Assert.Equal(120, config.DynamicMonsterSpawning.MaxMonstersPerMap);
            Assert.Equal("XGD_动态刷怪", config.DynamicMonsterSpawning.RefreshMonTrigger);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_ExistingFile_ReplacesWithNewConfigAndRemovesTemporaryFile()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "config.json");

        try
        {
            File.WriteAllText(path, "{\"DynamicMonsterSpawning\":{}}");
            var service = new AppConfigService(path);
            var config = service.LoadOrCreate();
            config.DynamicMonsterSpawning.RefreshMonInterval = 9;
            config.DynamicMonsterSpawning.MaxRefreshCount = 45;
            config.DynamicMonsterSpawning.MaxMonstersPerMap = 180;

            service.Save(config);
            var savedConfig = service.LoadOrCreate();

            Assert.Equal(9, savedConfig.DynamicMonsterSpawning.RefreshMonInterval);
            Assert.Equal(45, savedConfig.DynamicMonsterSpawning.MaxRefreshCount);
            Assert.Equal(180, savedConfig.DynamicMonsterSpawning.MaxMonstersPerMap);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"Legend2Tool-Config-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        return directory;
    }
}
