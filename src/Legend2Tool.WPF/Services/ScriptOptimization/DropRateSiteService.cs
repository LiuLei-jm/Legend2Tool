using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using System.IO;
using System.Text;

namespace Legend2Tool.WPF.Services.ScriptOptimization;

public sealed class DropRateSiteService(IEncodingService encodingService) : IDropRateSiteService
{
    private readonly object _gate = new();

    public bool HasCustomFile(string? directory) => !string.IsNullOrWhiteSpace(directory)
        && File.Exists(CustomPath(directory));

    public IReadOnlyList<DropRateVersion> Load(string directory)
    {
        lock (_gate) return Read(directory).Document.Versions;
    }

    public void Append(string directory, string name, string data, string dataContent)
    {
        lock (_gate)
        {
            var source = Read(directory);
            string dataPath = DataPath(directory, data);
            string updated = source.Document.Append(name, data);
            Commit(source, updated, dataPath, new UTF8Encoding(false).GetBytes(dataContent));
        }
    }

    public void Delete(string directory, DropRateVersion version)
    {
        lock (_gate)
        {
            var source = Read(directory);
            string dataPath = DataPath(directory, version.Data);
            string updated = source.Document.Delete(version);
            if (source.Document.Versions.Any(item => item.Index != version.Index
                && string.Equals(item.Data, version.Data, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("其他版本仍引用同一个 data 文件，无法删除该项目及数据文件。");
            Commit(source, updated, dataPath, null);
        }
    }

    private sealed record Source(string Path, byte[] Bytes, Encoding Encoding, byte[] Preamble, VersionListDocument Document);

    private Source Read(string directory)
    {
        string path = CustomPath(directory);
        byte[] bytes = File.ReadAllBytes(path);
        Encoding? detected = encodingService.DetectBom(bytes);
        if (detected is null)
        {
            try
            {
                detected = new UTF8Encoding(false, true);
                _ = detected.GetCharCount(bytes);
            }
            catch (DecoderFallbackException)
            {
                detected = encodingService.DetectFileEncoding(path, Encoding.GetEncoding("GB18030"));
            }
        }
        // Throw rather than replacing characters that cannot round-trip in the original encoding.
        Encoding encoding = Encoding.GetEncoding(detected.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        byte[] preamble = detected.GetPreamble();
        if (preamble.Length == 0 || !bytes.AsSpan().StartsWith(preamble)) preamble = [];
        string text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        return new Source(path, bytes, encoding, preamble, new VersionListDocument(text));
    }

    private static string CustomPath(string directory) => Path.Combine(directory, "js", "custom.js");

    private static string DataPath(string directory, string data)
    {
        if (string.IsNullOrWhiteSpace(data) || data is "." or ".."
            || data.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || data.EndsWith('.') || data.EndsWith(' '))
            throw new InvalidOperationException("版本 data 必须是有效的文件名，不能包含目录或路径。");
        string root = Path.GetFullPath(Path.Combine(directory, "data"));
        string path = Path.GetFullPath(Path.Combine(root, data + ".js"));
        if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("数据文件必须位于所选目录的 data 文件夹内。");
        // Avoid following junctions or symbolic links outside the selected data directory.
        if ((Directory.Exists(root) && File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            || (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
            throw new InvalidOperationException("data 目录及数据文件不能是符号链接或目录联接。");
        return path;
    }

    private static void Commit(Source source, string updated, string dataPath, byte[]? newData)
    {
        // Validate the resulting array before touching either file.
        _ = new VersionListDocument(updated);
        byte[] customBytes = [.. source.Preamble, .. source.Encoding.GetBytes(updated)];
        byte[]? previousData = File.Exists(dataPath) ? File.ReadAllBytes(dataPath) : null;
        string? customTemp = null;
        string? dataTemp = null;
        bool dataChanged = false;
        try
        {
            customTemp = Stage(source.Path, customBytes);
            if (newData is not null) dataTemp = Stage(dataPath, newData);
            if (!File.ReadAllBytes(source.Path).AsSpan().SequenceEqual(source.Bytes))
                throw new IOException("custom.js 已发生变化，请刷新后重试。");
            if (dataTemp is not null) File.Move(dataTemp, dataPath, overwrite: true);
            else if (File.Exists(dataPath)) File.Delete(dataPath);
            dataChanged = true;
            File.Move(customTemp, source.Path, overwrite: true);
        }
        catch (Exception original)
        {
            if (dataChanged)
            {
                try
                {
                    if (previousData is null)
                    {
                        if (File.Exists(dataPath)) File.Delete(dataPath);
                    }
                    else
                    {
                        string restore = Stage(dataPath, previousData);
                        try { File.Move(restore, dataPath, overwrite: true); }
                        finally { File.Delete(restore); }
                    }
                }
                catch (Exception rollback)
                {
                    throw new AggregateException("操作失败，数据文件恢复也失败，请检查 custom.js 和数据文件。", original, rollback);
                }
            }
            throw;
        }
        finally
        {
            if (customTemp is not null) File.Delete(customTemp);
            if (dataTemp is not null) File.Delete(dataTemp);
        }
    }

    private static string Stage(string destination, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            return temporary;
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }
}
