using CommunityToolkit.Mvvm.Messaging;
using IniFileParser.Model;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.BackList;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services;
using Serilog;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace Legend2Tool.WPF.State
{
    public class ConfigStore : IRecipient<ServerDirectoryChangedMessage>
    {
        private readonly IConfigService _configService;
        private readonly IEncodingService _encodingService;
        private EngineType _engineType;
        private ILogger _logger;
        private M2ConfigBase _m2Config = new M2ConfigBase();
        private List<BackListBase> _backLists = new List<BackListBase>();
        private Setup _setup = new Setup();

        public ConfigStore(IConfigService configService, IEncodingService encodingService, ILogger logger)
        {
            WeakReferenceMessenger.Default.Register<ServerDirectoryChangedMessage>(this);
            _configService = configService;
            _encodingService = encodingService;
            _logger = logger;
        }

        public string ServerDirectory { get; set; } = string.Empty;
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
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            _engineType = _configService.CheckEngineType(ServerDirectory);
            M2Config = _engineType switch
            {
                EngineType.GOM => _configService.ReadMultiSectionConfig<GOMConfig>(filePath, fileEncoding),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => _configService.ReadMultiSectionConfig<GEEConfig>(filePath, fileEncoding),
                EngineType.BLUE => _configService.ReadMultiSectionConfig<BLUEConfig>(filePath, fileEncoding),
                _ => throw new InvalidOperationException("不支持的引擎")
            };

            M2Config.GameDirectory = $"{ServerDirectory}\\";

            if (M2Config is GEEConfig geeConfig)
            {
                geeConfig.SqliteDBFile = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.SqliteDBFile, "Mud2"));
                geeConfig.SqliteDBName = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.SqliteDBName, "Mud2"));
                if (geeConfig.MyGetTxtNum > 0)
                {
                    for (int i = 0; i < geeConfig.MyGetTxtNum; i++)
                    {
                        geeConfig.MyGetTxtList[i] = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.MyGetTxtList[i], "Mir200"));
                    }
                }
                if (geeConfig.MyGetFileNum > 0)
                {
                    for (int i = 0; i < geeConfig.MyGetFileNum; i++)
                    {
                        geeConfig.MyGetFileList[i] = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.MyGetFileList[i], "Mir200"));
                    }
                }
                if (geeConfig.MyGetDirNum > 0)
                {
                    for (int i = 0; i < geeConfig.MyGetDirNum; i++)
                    {
                        geeConfig.MyGetDirList[i] = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.MyGetDirList[i], "Mir200"));
                    }
                }

                SetDefaultBackListPath();
                SetDefaultSetupPath();
            }
            else if (M2Config is GOMConfig gomConfig)
            {
                gomConfig.AccessFileName = Path.Combine(ServerDirectory, GetSourcePath(gomConfig.AccessFileName, "Mud2"));
                SetDefaultBackListPath();
                SetDefaultSetupPath();
            }
            else if (M2Config is BLUEConfig blueConfig)
            {
                blueConfig.DataTableFile = Path.Combine(ServerDirectory, GetSourcePath(blueConfig.DataTableFile, "Mud2"));
                blueConfig.Backup = 1;
                blueConfig.Mode = 0;
                blueConfig.Interval = 720;
                blueConfig.Attime = "0:0:00";
                blueConfig.数据备份目录 = 1;
                blueConfig.数据备份目录_path = Path.Combine(ServerDirectory, "数据备份");
                blueConfig.WinRAR目录 = 1;
                blueConfig.WinRAR目录_path = Path.Combine(ServerDirectory, "WinRAR");
                blueConfig.FDB目录 = 1;
                blueConfig.FDB目录_path = Path.Combine(ServerDirectory, "DBServer", "FDB");
                blueConfig.IDDB目录 = 1;
                blueConfig.IDDB目录_path = Path.Combine(ServerDirectory, "LoginSrv", "IDDB");
                blueConfig.行会目录 = 1;
                blueConfig.行会目录_path = Path.Combine(ServerDirectory, "Mir200", "GuildBase");
                blueConfig.沙城目录 = 1;
                blueConfig.沙城目录_path = Path.Combine(ServerDirectory, "Mir200", "Castle");
                blueConfig.脚本数据目录 = 1;
                blueConfig.脚本数据目录_path = Path.Combine(ServerDirectory, "Mir200", "Envir", "QuestDiary", "数据文件");
            }
            else
            {
                _logger.Warning($"尝试读取未知的 M2Config 类型：{M2Config?.GetType().Name ?? "null"}");
            }

        }

        private void SetDefaultSetupPath()
        {
            var filePath = Path.Combine(ServerDirectory, "Mir200", "!Setup.txt");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);

            _setup = _configService.ReadMultiSectionConfig<Setup>(filePath, fileEncoding);

            _setup.ChatDir = Path.Combine(ServerDirectory, "Mir200", "ChatLog");
            _setup.SortDir = Path.Combine(ServerDirectory, "Mir200", "Sort");
            _setup.BoxsDir = Path.Combine(ServerDirectory, "Mir200", "Envir", "Boxs");
            _setup.BoxsFile = Path.Combine(ServerDirectory, "Mir200", "Envir", "Boxs", "BoxsList.txt");

            _configService.WriteMultiSectionConfig(filePath, _setup, fileEncoding);
        }

        private void SetDefaultBackListPath()
        {
            string filePath = Path.Combine(ServerDirectory, "BackList.txt");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);

            var parser = new IniFileParser.IniFileParser();
            parser.Parser.Configuration.AssigmentSpacer = ""; // <-- THIS IS THE KEY FIX
            parser.Parser.Configuration.CommentString = "#"; // Good practice
            parser.Parser.Configuration.SkipInvalidLines = true; // Good practice


            IniData data = parser.ReadFile(filePath, fileEncoding);

            _backLists.Clear();


            foreach (var section in data.Sections)
            {
                if (int.TryParse(section.SectionName, out _))
                {
                    var backList = new BackListBase();
                    backList = _engineType switch
                    {
                        EngineType.GOM => _configService.ReadSectionConfig<BackListBase>(filePath, fileEncoding, section.SectionName),
                        EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => _configService.ReadSectionConfig<GEEBackList>(filePath, fileEncoding, section.SectionName),
                        _ => throw new InvalidOperationException("不支持的引擎")
                    };
                    backList.sectionName = section.SectionName;
                    _backLists.Add(backList);
                }
            }

            foreach (var backList in _backLists)
            {
                backList.Source = Path.Combine(ServerDirectory, GetSourcePath(backList.Source, "mirserver")[10..]);
                backList.Save = Path.Combine(ServerDirectory, GetSourcePath(backList.Save, "mirserver")[10..]);
                backList.Hour = 6;
                backList.Min = 0;
                backList.BackMode = 1;
                backList.GetBack = 1;
                if (backList is GEEBackList geeBackList)
                {
                    geeBackList.IsCompress = 1;
                    _configService.WriteSectionConfig<GEEBackList>(filePath, geeBackList, fileEncoding, backList.sectionName);
                    continue;
                }
                _configService.WriteSectionConfig<BackListBase>(filePath, backList, fileEncoding, backList.sectionName);
            }
        }

        private string GetSourcePath(string? oldPath, string match)
        {
            int startIndex = oldPath!.IndexOf(match, StringComparison.OrdinalIgnoreCase);
            if (startIndex != -1)
            {
                return oldPath[startIndex..];
            }
            return oldPath ?? string.Empty;
        }

        private void SaveM2ConfigToFile()
        {
            string filePath = Path.Combine(ServerDirectory, "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);


            if (M2Config is GEEConfig geeConfig)
            {
                _configService.WriteMultiSectionConfig(filePath, geeConfig, fileEncoding);
            }
            else if (M2Config is GOMConfig gomConfig)
            {
                _configService.WriteMultiSectionConfig(filePath, gomConfig, fileEncoding);
            }
            else if (M2Config is BLUEConfig blueConfig)
            {
                _configService.WriteMultiSectionConfig(filePath, blueConfig, fileEncoding);
            }
            else
            {
                _logger.Warning($"尝试写入未知的 M2Config 类型：{M2Config?.GetType().Name ?? "null"}");
            }
        }

        public async Task<string> GetExternalIpAddressAsync()
        {
            string ip = await _configService.GetExternalIpAddressAsync();
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("无法获取外部IP地址");
                return M2Config.ExtIPaddr ?? string.Empty;
            }
            return ip;
        }
        public void SaveConfigFile()
        {
            int[] portsToCheck =
            [
                _m2Config.DBServerGatePort,
                _m2Config.DBServerServerPort,
                _m2Config.M2ServerGatePort,
                _m2Config.M2ServerMsgSrvPort,
               _m2Config.RunGateGatePort1,
                _m2Config.LoginGateGatePort,
                _m2Config.SelGateGatePort,
                _m2Config.LoginServerGatePort,
                _m2Config.LoginServerServerPort,
                _m2Config.LogServerPort,
            ];

            if (M2Config is GEEConfig geeConfig)
            {
                portsToCheck = portsToCheck.Concat(new[]
                {
                    geeConfig.LoginGateGatePort1,
                    geeConfig.RunGateDBPort1,
                    geeConfig.RunGateDBPort2,
                    geeConfig.RunGateDBPort3,
                    geeConfig.RunGateDBPort4,
                    geeConfig.RunGateDBPort5,
                    geeConfig.RunGateDBPort6,
                    geeConfig.RunGateDBPort7,
                    geeConfig.RunGateDBPort8
                }).ToArray();
            }

            if (M2Config is BLUEConfig blueConfig)
            {
                portsToCheck = portsToCheck.Concat(new[] { blueConfig.LoginServerMonPort }).ToArray();
            }

            if (!CheckPorts(portsToCheck))
            {
                return;
            }

            SaveM2ConfigToFile();
        }

        private bool CheckPorts(int[] portsToCheck)
        {
            var results = portsToCheck
                .AsParallel()
                .Select(port => new { Port = port, InUse = IsPortInUse(port) })
                .ToList();

            foreach (var result in results)
            {
                if (result.InUse)
                {
                    MessageBox.Show(
                        $"端口 {result.Port} 已经被使用.",
                        "警告",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return false;
                }
            }
            return true;
        }

        private bool IsPortInUse(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
