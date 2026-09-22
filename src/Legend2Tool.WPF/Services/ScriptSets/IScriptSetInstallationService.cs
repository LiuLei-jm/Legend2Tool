using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services.ScriptSets
{
    public interface IScriptSetInstallationService
    {
        Task<ScriptSetInstallationResult> InstallAsync(
            ScriptSetInfo scriptSet,
            CancellationToken cancellationToken = default
        );

        Task<ScriptSetRemovalResult> RemoveAsync(
            ScriptSetInfo scriptSet,
            CancellationToken cancellationToken = default
        );
    }
}
