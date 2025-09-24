using Legend2Tool.WPF.Models.ScriptOptimizations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Services
{
    public interface IDynamicMonsterSpawningService
    {
        Task GenerateRefreshMonScriptAsync(RefreshOptimizationOptions options);
        Task ClearRefreshMonScriptAsync(RefreshOptimizationOptions options);
    }
}
