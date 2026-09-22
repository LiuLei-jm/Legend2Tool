using System.Diagnostics;
using System.IO;
using Legend2Tool.WPF.Enums;

namespace Legend2Tool.WPF.Services.ServerConfiguration.Engine;

internal sealed class EngineTypeDetector
{
    public EngineType Detect(string serverDirectory)
    {
        string primaryPath = Path.Combine(serverDirectory, "GameOfMir引擎控制器.exe");
        string filePath = File.Exists(primaryPath)
            ? primaryPath
            : Path.Combine(serverDirectory, "GameCenter.exe");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("指定的文件不存在", filePath);

        FileVersionInfo info = FileVersionInfo.GetVersionInfo(filePath);
        var indicators = new (string Keyword, EngineType Type)[]
        {
            ("gameofmir", EngineType.GOM), ("gee", EngineType.GEE),
            ("gxx", EngineType.GXX), ("hao", EngineType.LF),
            ("v8", EngineType.V8), ("blue", EngineType.BLUE),
            ("hge", EngineType.HGE), ("gamecenter", EngineType.NEWGOM)
        };
        string metadata = $"{info.CompanyName} {info.FileDescription}";
        foreach (var indicator in indicators)
            if (metadata.Contains(indicator.Keyword, StringComparison.OrdinalIgnoreCase))
                return indicator.Type;
        return EngineType.Unknown;
    }
}
