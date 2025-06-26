using CommunityToolkit.Mvvm.Messaging;
using IniFileParser.Model;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.ViewModels;
using System.IO;

namespace Legend2Tool.WPF.State
{
    public class ConfigStore : IRecipient<ServerDirectoryChangedMessage>
    {
        private readonly IConfigService _configService;
        private readonly IEncodingService _encodingService;
        private EngineType _engineType;

        public ConfigStore(IConfigService configService, IEncodingService encodingService)
        {
            WeakReferenceMessenger.Default.RegisterAll(this);
            _configService = configService;
            _encodingService = encodingService;
        }

        public string ServerDirectory { get; set; } = string.Empty;
        public M2ConfigBase M2Config { get; set; } = new M2ConfigBase();
        public EngineType EngineType { get; set; }
        public void Receive(ServerDirectoryChangedMessage message)
        {
            if (!string.Equals(ServerDirectory, message.Value, StringComparison.OrdinalIgnoreCase))
            {
                ServerDirectory = message.Value;
                GetM2ConfigInfo();
            }
        }

        private void GetM2ConfigInfo()
        {
            string filePath = Path.Combine(ServerDirectory, "config.ini");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("指定文件不存在", filePath);
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            _engineType = _configService.CheckEngineType(ServerDirectory);
            M2Config = _engineType switch
            {
                EngineType.GOM => _configService.ReadMultiSectionConfig<GOMConfig>(filePath, fileEncoding),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => _configService.ReadMultiSectionConfig<GEEConfig>(filePath, fileEncoding),
                EngineType.BLUE => _configService.ReadMultiSectionConfig<BLUEConfig>(filePath, fileEncoding),
                _ => throw new InvalidOperationException("Unsupported engine type")
            }
        ;
        }
    }
}
