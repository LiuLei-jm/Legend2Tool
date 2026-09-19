using System.Text;
using Legend2Tool.WPF.Services;
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
