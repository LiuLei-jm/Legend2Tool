using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.State;

namespace Legend2Tool.WPF.Services
{
    public interface IConfigService
    {
        EngineType CheckEngineType(string serverDirectory);
        Task<string> GetExternalIpAddressAsync();
        bool CheckPorts(int[] portsToCheck);
        string GetResourcesDirByGamePinyin(string launcherName);
        string GetLauncherName(ConfigStore configStore);
        Task SaveConfigFileAsync(ConfigStore configStore);
        LoadedServerConfig LoadServerConfig(string serverDirectory);
        void ApplyDefaultAuxiliarySettings(ConfigStore configStore);
        Task GenerateCleanupScriptAsync(string baseDirectory);
    }
}
