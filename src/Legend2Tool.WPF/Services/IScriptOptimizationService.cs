using Legend2Tool.WPF.Models.ScriptOptimizations;

namespace Legend2Tool.WPF.Services
{
    public interface IScriptOptimizationService
    {
        Task GenerateRefreshMonScriptAsync(RefreshOptimizationOptions options);
        Task ClearRefreshMonScriptAsync(RefreshOptimizationOptions options);
        Task<List<DuplicatedTriggerEntry>> DetectDuplicatedTriggerAsync();
        Task OptimizingCallsAsync();
        Task DropRateCalculatorAsync();
        void OpenFile(DuplicatedTriggerEntry entry);
        void UpdateMainCityLists(IEnumerable<string> mapCodes);
    }
}
