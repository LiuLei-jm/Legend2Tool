using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.ViewModels;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class PortConfViewModelTests
{
    [Fact]
    public void DatabasePathMapping_SupportedEngines_UsesEngineSpecificProperty()
    {
        var gomConfig = new GOMConfig();
        PortConfViewModel.SetDatabasePath(EngineType.GOM, gomConfig, "gom.mdb");
        Assert.Equal(
            "gom.mdb",
            PortConfViewModel.GetDatabasePath(EngineType.GOM, gomConfig)
        );

        var newGomConfig = new GOMConfig();
        PortConfViewModel.SetDatabasePath(
            EngineType.NEWGOM,
            newGomConfig,
            "new-gom.mdb"
        );
        Assert.Equal(
            "new-gom.mdb",
            PortConfViewModel.GetDatabasePath(EngineType.NEWGOM, newGomConfig)
        );

        foreach (
            EngineType engineType in new[]
            {
                EngineType.GEE,
                EngineType.GXX,
                EngineType.LF,
                EngineType.V8
            }
        )
        {
            var geeConfig = new GEEConfig();
            string path = $"{engineType}.db";
            PortConfViewModel.SetDatabasePath(engineType, geeConfig, path);
            Assert.Equal(
                path,
                PortConfViewModel.GetDatabasePath(engineType, geeConfig)
            );
        }

        var blueConfig = new BLUEConfig();
        PortConfViewModel.SetDatabasePath(EngineType.BLUE, blueConfig, "blue.db");
        Assert.Equal(
            "blue.db",
            PortConfViewModel.GetDatabasePath(EngineType.BLUE, blueConfig)
        );

        var hgeConfig = new HGEConfig();
        PortConfViewModel.SetDatabasePath(EngineType.HGE, hgeConfig, "hge.db");
        Assert.Equal(
            "hge.db",
            PortConfViewModel.GetDatabasePath(EngineType.HGE, hgeConfig)
        );
    }
}
