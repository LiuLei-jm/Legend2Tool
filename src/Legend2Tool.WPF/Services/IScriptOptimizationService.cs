using Legend2Tool.WPF.Models.ScriptOptimizations;

namespace Legend2Tool.WPF.Services
{
    public interface IScriptOptimizationService
    {
        void UpdateMainCityLists(string mainCityLists);
        Task<List<DuplicatedTriggerEntry>> DetectDuplicatedTriggerAsync();
        Task OptimizingCallsAsync();
        Task DropRateCalculatorAsync();
        void OpenFile(DuplicatedTriggerEntry entry);
        Task OptimizingMinMonBurstRateAsync(int minMonBurstRate);
    }
}
