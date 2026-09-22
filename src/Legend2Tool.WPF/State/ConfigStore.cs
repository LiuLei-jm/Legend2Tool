using CommunityToolkit.Mvvm.Messaging;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.BackList;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Services.ServerConfiguration;
using Serilog;
using System.Windows;
using MessageBox = HandyControl.Controls.MessageBox;

namespace Legend2Tool.WPF.State
{
    public class ConfigStore : IRecipient<ServerDirectoryChangedMessage>, IRecipient<PatchDirectoryChangedMessage>
    {
        private readonly IConfigService _configService;
        private readonly ILogger _logger;
        private M2ConfigBase _m2Config = new();
        private LauncherConfigBase _launcherConfig = new();

        public ConfigStore(IConfigService configService, ILogger logger)
        {
            WeakReferenceMessenger.Default.Register<ServerDirectoryChangedMessage>(this);
            WeakReferenceMessenger.Default.Register<PatchDirectoryChangedMessage>(this);
            _configService = configService;
            _logger = logger;
        }

        public string ServerDirectory { get; private set; } = string.Empty;
        public string PatchDirectory { get; set; } = string.Empty;

        public M2ConfigBase M2Config => _m2Config;

        public LauncherConfigBase LauncherConfig => _launcherConfig;
        public Setup Setup { get; private set; } = new();

        public List<BackListBase> BackLists { get; private set; } = [];
        public EngineType EngineType { get; private set; }
        internal bool AuxiliaryDefaultsPending { get; set; }

        public string MainCityLists { get; set; }

        public void Receive(ServerDirectoryChangedMessage message)
        {
            try
            {
                LoadedServerConfig loaded = _configService.LoadServerConfig(message.Value);
                ServerDirectory = loaded.ServerDirectory;
                EngineType = loaded.EngineType;
                _m2Config = loaded.M2Config;
                _launcherConfig = loaded.LauncherConfig;
                Setup = loaded.Setup;
                BackLists = loaded.BackLists;
                AuxiliaryDefaultsPending = false;
                WeakReferenceMessenger.Default.Send(new M2ConfigChangedMessage());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取配置文件信息时发生错误");
                MessageBox.Show($"获取配置文件信息时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Receive(PatchDirectoryChangedMessage message)
        {
            if (!string.Equals(PatchDirectory, message.Value, StringComparison.OrdinalIgnoreCase))
            {
                PatchDirectory = message.Value;
            }
        }
    }
}
