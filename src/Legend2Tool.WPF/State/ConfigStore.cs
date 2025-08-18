using CommunityToolkit.Mvvm.Messaging;
using HandyControl.Controls;
using IniFileParser.Model;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.BackList;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services;
using Serilog;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using TinyPinyin;
using MessageBox = HandyControl.Controls.MessageBox;

namespace Legend2Tool.WPF.State
{
    public class ConfigStore : IRecipient<ServerDirectoryChangedMessage>, IRecipient<PatchDirectoryChangedMessage>
    {
        private readonly IConfigService _configService;
        private readonly ILogger _logger;
        private M2ConfigBase _m2Config = new();
        private LauncherConfigBase _launcherConfig = new();

        public ConfigStore(IConfigService configService,  ILogger logger)
        {
            WeakReferenceMessenger.Default.Register<ServerDirectoryChangedMessage>(this);
            WeakReferenceMessenger.Default.Register<PatchDirectoryChangedMessage>(this);
            _configService = configService;
            _logger = logger;
        }

        public string ServerDirectory { get; set; } = string.Empty;
        public string PatchDirectory { get; set; } = string.Empty;

        public M2ConfigBase M2Config
        {
            get => _m2Config;
            set
            {
                if (_m2Config != value)
                {
                    _m2Config = value;
                    WeakReferenceMessenger.Default.Send(new M2ConfigChangedMessage());
                }
            }
        }

        public LauncherConfigBase LauncherConfig
        {
            get => _launcherConfig;
            set
            {
                if (_launcherConfig != value)
                {
                    _launcherConfig = value;
                    WeakReferenceMessenger.Default.Send(new M2ConfigChangedMessage());
                }
            }
        }
        public Setup Setup { get; set; } = new(); 

        public List<BackListBase> BackLists = [];
        public EngineType EngineType { get; set; }

        public void Receive(ServerDirectoryChangedMessage message)
        {
            if (!string.Equals(ServerDirectory, message.Value, StringComparison.OrdinalIgnoreCase))
            {
                ServerDirectory = message.Value;
                try
                {
                    _configService.GetM2ConfigInfo(this);
                    if (EngineType != EngineType.BLUE && EngineType != EngineType.NEWGOM)
                        _configService.GetLauncherConfigInfo(this);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "获取配置文件信息时发生错误");
                    MessageBox.Show($"获取配置文件信息时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
