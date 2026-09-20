using Legend2Tool.WPF.Models.ScriptOptimizations;

namespace Legend2Tool.WPF.Services
{
    public interface IDynamicMonsterSpawningService
    {
        Task<IReadOnlyList<DynamicMonsterSpawningResult>> GenerateRefreshMonScriptAsync(
            RefreshOptimizationOptions options
        );
        Task ClearRefreshMonScriptAsync(RefreshOptimizationOptions options);
    }
}
