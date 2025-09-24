using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.State;
using System.Text;

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
        void GetM2ConfigInfo(ConfigStore configStore);
        void GetLauncherConfigInfo(ConfigStore configStore);
        Task GenerateCleanupScriptAsync(string baseDirectory);
    }
}
