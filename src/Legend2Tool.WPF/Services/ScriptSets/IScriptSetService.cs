using Legend2Tool.WPF.Models.ScriptSets;
using System.IO;

namespace Legend2Tool.WPF.Services.ScriptSets
{
    public interface IScriptSetService
    {
        Task<IReadOnlyList<ScriptSetInfo>> GetScriptSetsAsync(
            CancellationToken cancellationToken = default
        );

        Task<ScriptSetDeploymentData> GetDeploymentDataAsync(
            Guid scriptSetId,
            CancellationToken cancellationToken = default
        );

        Task DownloadMaterialFileAsync(
            Guid materialFileId,
            Stream destination,
            CancellationToken cancellationToken = default
        );
    }
}
