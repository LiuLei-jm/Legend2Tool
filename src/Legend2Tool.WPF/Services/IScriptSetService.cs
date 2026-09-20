using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services
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
    }
}
