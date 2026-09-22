using Legend2Tool.WPF.Services.Infrastructure.Files;
using Legend2Tool.WPF.State;
using System.IO;

namespace Legend2Tool.WPF.Services.ScriptOptimization.Modular;

internal sealed class ScriptFileCatalog
{
    private readonly ConfigStore _configStore;
    private readonly IFileService _fileService;

    public ScriptFileCatalog(ConfigStore configStore, IFileService fileService)
    {
        _configStore = configStore;
        _fileService = fileService;
    }

    public List<string> GetScriptFiles()
    {
        var files = new List<string>();
        var directories = new[]
        {
            Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MapQuest_Def"),
            Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Market_Def"),
            Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary"),
            Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_Def")
        };

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"目录{directory}没有找到");
            }

            files.AddRange(_fileService.GetFiles(directory, ["*.txt"], SearchOption.AllDirectories));
        }

        return files;
    }
}
