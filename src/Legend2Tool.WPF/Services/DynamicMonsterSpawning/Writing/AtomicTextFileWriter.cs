using System.Text;
using System.IO;

namespace Legend2Tool.WPF.Services.DynamicMonsterSpawning.Writing;

internal sealed class AtomicTextFileWriter
{
    public async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding encoding)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, lines, encoding);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
