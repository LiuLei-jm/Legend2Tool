using Legend2Tool.WPF.Models.ScriptOptimizations;

namespace Legend2Tool.WPF.Services.ScriptOptimization;

public interface IDropRateSiteService
{
    bool HasCustomFile(string? directory);
    IReadOnlyList<DropRateVersion> Load(string directory);
    void Append(string directory, string name, string data, string dataContent);
    void Delete(string directory, DropRateVersion version);
}
