using System.Text;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ServerConfiguration;
using Serilog;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public class ConfigReadingTests
{
    static ConfigReadingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void ReadGeeConfig_MissingIndexedItem_SkipsEmptyItem()
    {
        using var file = new TemporaryIniFile(
            "[ClearServer]\nMyGetTxtNum=2\nMyGetTxt0=Mir200\\A.txt\n"
        );
        var service = CreateService();

        GEEConfig config = service.ReadMultiSectionConfig<GEEConfig>(
            file.Path,
            Encoding.UTF8
        );

        Assert.Equal([@"Mir200\A.txt"], config.MyGetTxtList);
    }

    [Fact]
    public void ReadServerConfig_Gb18030GameName_ReturnsChineseText()
    {
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        using var file = new TemporaryIniFile("[GameConf]\nGameName=魔女探秘一区\n", gb18030);
        var service = CreateService();

        GEEConfig config = service.ReadServerConfig<GEEConfig>(file.Path);

        Assert.Equal("魔女探秘一区", config.GameName);
    }

    [Fact]
    public void ReadGeeConfig_EmptyIndexedCount_UsesDefaultEmptyList()
    {
        using var file = new TemporaryIniFile("[ClearServer]\nMyGetTxtNum=\n");
        var service = CreateService();

        GEEConfig config = service.ReadMultiSectionConfig<GEEConfig>(
            file.Path,
            Encoding.UTF8
        );

        Assert.Equal(0, config.MyGetTxtNum);
        Assert.Empty(config.MyGetTxtList);
    }

    [Fact]
    public void ReadGeeConfig_InvalidIndexedCount_UsesEmptyList()
    {
        using var file = new TemporaryIniFile(
            "[ClearServer]\nMyGetTxtNum=-1\nMyGetTxt0=Mir200\\A.txt\n"
        );
        var service = CreateService();

        GEEConfig config = service.ReadMultiSectionConfig<GEEConfig>(
            file.Path,
            Encoding.UTF8
        );

        Assert.Empty(config.MyGetTxtList);
    }

    [Fact]
    public void ReadGeeConfig_InvalidNumber_ReportsSectionAndKey()
    {
        using var file = new TemporaryIniFile("[DBServer]\nGatePort=invalid\n");
        var service = CreateService();

        var error = Assert.Throws<InvalidDataException>(
            () => service.ReadMultiSectionConfig<GEEConfig>(file.Path, Encoding.UTF8)
        );

        Assert.Contains("[DBServer] GatePort", error.Message);
    }

    [Fact]
    public void WriteGeeConfig_UpdatesCountAndRemovesStaleIndexedKeys()
    {
        using var file = new TemporaryIniFile(
            "[ClearServer]\nMyGetTxtNum=2\nMyGetTxt0=old0\nMyGetTxt1=old1\n"
        );
        var service = CreateService();
        var config = new GEEConfig();
        config.MyGetTxtList.Add("new0");

        service.WriteMultiSectionConfig(file.Path, config, Encoding.UTF8);

        string written = File.ReadAllText(file.Path, Encoding.UTF8);
        Assert.Contains("MyGetTxtNum=1", written);
        Assert.Contains("MyGetTxt0=new0", written);
        Assert.DoesNotContain("MyGetTxt1", written);
    }

    [Fact]
    public void ResolveServerPath_MatchesDirectorySegmentOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), "current-server");
        string oldPath = @"C:\old\Mud2\Data\items.db";

        string resolved = ConfigPathResolver.ResolveServerPath(root, oldPath, "Mud2");

        Assert.Equal(Path.Combine(root, "Mud2", "Data", "items.db"), resolved);
        Assert.Equal(
            string.Empty,
            ConfigPathResolver.ResolveServerPath(root, null, "Mud2")
        );
    }

    [Fact]
    public void ResolveBackListPath_RelocatesPathAfterMirserverSegment()
    {
        string root = Path.Combine(Path.GetTempPath(), "current-server");

        string resolved = ConfigPathResolver.ResolveBackListPath(
            root, @"C:\old\mirserver\Mir200\Envir"
        );

        Assert.Equal(Path.Combine(root, "Mir200", "Envir"), resolved);
    }

    [Fact]
    public void DetectBom_RecognizesUtf32BeforeUtf16()
    {
        var encoding = new EncodingService().DetectBom([0xFF, 0xFE, 0x00, 0x00]);

        Assert.Equal(Encoding.UTF32.CodePage, encoding.CodePage);
    }

    private static ConfigService CreateService() => new(
        Log.Logger,
        new EncodingService(),
        null!
    );

    private sealed class TemporaryIniFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"Legend2Tool-{Guid.NewGuid():N}.ini"
        );

        public TemporaryIniFile(string contents)
            : this(contents, Encoding.UTF8) { }

        public TemporaryIniFile(string contents, Encoding encoding)
        {
            File.WriteAllText(Path, contents, encoding);
        }

        public void Dispose() => File.Delete(Path);
    }
}
