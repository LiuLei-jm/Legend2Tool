using System.Text;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class EncodingServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Legend2Tool-Encoding-{Guid.NewGuid():N}");
    private readonly EncodingService _service = new();

    public EncodingServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void DetectFileEncodingResult_EmptyFile_IsUnknown()
    {
        string path = CreateFile("empty.txt", []);

        EncodingDetectionResult result = _service.DetectFileEncodingResult(path);

        Assert.False(result.IsKnown);
        Assert.Contains("空文件", result.Reason);
    }

    [Fact]
    public void DetectFileEncodingResult_AsciiOnly_IsUnknown()
    {
        string path = CreateFile("ascii.txt", Encoding.ASCII.GetBytes("123 ABC"));

        EncodingDetectionResult result = _service.DetectFileEncodingResult(path);

        Assert.False(result.IsKnown);
        Assert.Contains("ASCII", result.Reason);
    }

    [Fact]
    public void DetectFileEncodingResult_ShortUtf8_UsesStrictValidation()
    {
        string path = CreateFile("utf8.txt", new UTF8Encoding(false).GetBytes("中文"));

        EncodingDetectionResult result = _service.DetectFileEncodingResult(path);

        Assert.True(result.IsKnown);
        Assert.Equal(Encoding.UTF8.CodePage, result.Encoding!.CodePage);
    }

    [Fact]
    public void DetectFileEncodingResult_ShortGb18030_UsesStrictValidation()
    {
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        string path = CreateFile("gb.txt", gb18030.GetBytes("中文"));

        EncodingDetectionResult result = _service.DetectFileEncodingResult(path);

        Assert.True(result.IsKnown);
        Assert.Equal(gb18030.CodePage, result.Encoding!.CodePage);
    }

    [Fact]
    public void DetectFileEncodingResult_Gb18030AfterInitialAscii_InspectsBeyondEightKilobytes()
    {
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        string asciiPrefix = string.Concat(Enumerable.Repeat("A=1\r\n", 2_000));
        string chineseContent = ";传奇世界游戏服务器配置数据库连接地图怪物角色账号登录设置物品装备技能任务管理系统测试中文内容\r\n";
        string path = CreateFile(
            "gb-after-ascii.txt",
            gb18030.GetBytes(asciiPrefix + chineseContent)
        );

        EncodingDetectionResult result = _service.DetectFileEncodingResult(path);

        Assert.True(result.IsKnown);
        Assert.Equal(gb18030.CodePage, result.Encoding!.CodePage);
        Assert.Contains("UDE", result.Reason);
    }

    [Fact]
    public void DetectFileEncodingResult_LowConfidenceGb18030_UsesStrictValidation()
    {
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        string asciiContent = string.Concat(Enumerable.Repeat("A=1\r\n", 1_200));
        string chineseContent = string.Concat(Enumerable.Repeat(";输入注册码\r\n", 20));
        string path = CreateFile(
            "low-confidence-gb.txt",
            gb18030.GetBytes(asciiContent + chineseContent)
        );

        EncodingDetectionResult result = _service.DetectFileEncodingResult(path);

        Assert.True(result.IsKnown);
        Assert.Equal(gb18030.CodePage, result.Encoding!.CodePage);
        Assert.Contains("严格 UTF-8 校验失败", result.Reason);
    }

    [Fact]
    public void DetectFileEncoding_LowConfidenceMongen_UsesProvidedFallback()
    {
        Encoding gb18030 = Encoding.GetEncoding("GB18030");
        string[] monsterNames =
        [
            "暗之魔龙教皇",
            "触龙神8",
            "魔龙刺蛙",
            "魔龙刀兵",
            "魔龙教主",
            "魔龙巨蛾",
            "魔龙力士",
            "魔龙破甲兵",
            "魔龙射手",
            "魔龙石碑",
            "魔龙树妖",
            "魔龙邪眼",
            "魔龙战将",
            "魔龙教皇"
        ];
        var content = new StringBuilder(";地图名：魔龙\r\n");
        for (int index = 0; index < 200; index++)
        {
            content.AppendLine(
                $"61\t51\t32\t{monsterNames[index % monsterNames.Length]}\t100\t50\t65"
            );
        }
        string path = CreateFile("mongen.txt", gb18030.GetBytes(content.ToString()));

        EncodingDetectionResult detection = _service.DetectFileEncodingResult(path);
        Encoding result = _service.DetectFileEncoding(path, gb18030);

        Assert.False(detection.IsKnown);
        Assert.Equal(gb18030.CodePage, result.CodePage);
        Assert.Contains("魔龙刀兵", File.ReadAllText(path, result));
    }

    [Fact]
    public void DetectBom_Utf32BigEndian_ReturnsBigEndianEncoding()
    {
        Encoding result = _service.DetectBom([0x00, 0x00, 0xFE, 0xFF]);

        Assert.Equal(12001, result.CodePage);
    }

    [Fact]
    public void ConvertFileEncoding_CreatesByteExactBackupAndConvertsOriginal()
    {
        Encoding sourceEncoding = Encoding.GetEncoding("GB18030");
        byte[] originalBytes = sourceEncoding.GetBytes("测试内容");
        string sourcePath = CreateFile(Path.Combine("Envir", "sample.txt"), originalBytes);
        string backupPath = Path.Combine(_directory, "ConvertBackup", "run", "sample.txt");

        _service.ConvertFileEncoding(sourcePath, sourcePath, sourceEncoding, "UTF-8", backupPath);

        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));
        Assert.Equal("测试内容", File.ReadAllText(sourcePath, new UTF8Encoding(false, true)));
        Assert.False(File.ReadAllBytes(sourcePath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void ConvertFileEncoding_InvalidSourceBytes_DoesNotReplaceOriginal()
    {
        byte[] originalBytes = [0xFF];
        string sourcePath = CreateFile("invalid.txt", originalBytes);
        string backupPath = Path.Combine(_directory, "ConvertBackup", "invalid.txt");

        Assert.Throws<DecoderFallbackException>(() =>
            _service.ConvertFileEncoding(sourcePath, sourcePath, new UTF8Encoding(false), "GB18030", backupPath));

        Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));
    }

    private string CreateFile(string relativePath, byte[] contents)
    {
        string path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
