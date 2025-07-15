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
        private readonly IEncodingService _encodingService;
        private readonly IFileService _fileService;
        private readonly ILogger _logger;
        private EngineType _engineType;
        private M2ConfigBase _m2Config = new();
        private LauncherConfigBase _launcherConfig = new();
        private List<BackListBase> _backLists = [];
        private Setup _setup = new();

        public ConfigStore(IConfigService configService, IEncodingService encodingService, ILogger logger, IFileService fileService)
        {
            WeakReferenceMessenger.Default.Register<ServerDirectoryChangedMessage>(this);
            WeakReferenceMessenger.Default.Register<PatchDirectoryChangedMessage>(this);
            _configService = configService;
            _encodingService = encodingService;
            _logger = logger;
            _fileService = fileService;
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

        public EngineType EngineType { get; set; }

        public void Receive(ServerDirectoryChangedMessage message)
        {
            if (!string.Equals(ServerDirectory, message.Value, StringComparison.OrdinalIgnoreCase))
            {
                ServerDirectory = message.Value;
                GetM2ConfigInfo();
                GetLauncherConfigInfo();
            }
        }

        public void Receive(PatchDirectoryChangedMessage message)
        {
            if (!string.Equals(PatchDirectory, message.Value, StringComparison.OrdinalIgnoreCase))
            {
                PatchDirectory = message.Value;
            }
        }

        public string GetLauncherName()
        {
            string pattern = @"^(.*?)[\d一二三四五六七八九十]+区";
            if (string.IsNullOrEmpty(M2Config.GameName)) M2Config.GameName = "热血传奇";
            var match = Regex.Match(M2Config.GameName, pattern);
            var launcherName = match.Groups[1].Value;
            return launcherName;
        }

        public string GetResourcesDirByGamePinyin(string launcherName)
        {
            if (String.IsNullOrEmpty(launcherName))
            {
                _logger.Warning("GetResourcesDirByGamePinyin 被调用，但 launcherName 为空。");
                return string.Empty;
            }
            var resourcesDir = PinyinHelper.GetPinyin(launcherName);
            resourcesDir = CaptalizeFirstLetters(resourcesDir.ToLower());
            return resourcesDir;
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
            SaveLauncherConfigToFile();
        }

        public void RenamePatchDirectory(string resourcesDir)
        {
            if (string.IsNullOrEmpty(PatchDirectory))
            {
                MessageBox.Show("补丁目录未设置");
                return;
            }
            if (!Directory.Exists(PatchDirectory))
            {
                MessageBox.Show($"补丁目录不存在：{PatchDirectory}");
                return;
            }
            string newPatchDir = Path.Combine(Path.GetDirectoryName(PatchDirectory) ?? string.Empty, resourcesDir);
            if (newPatchDir == PatchDirectory)
            {
                Growl.Info("补丁目录已是最新名称");
                return;
            }
            try
            {
                Directory.Move(PatchDirectory, newPatchDir);
                PatchDirectory = newPatchDir;
                WeakReferenceMessenger.Default.Send(new PatchDirectoryChangedMessage(newPatchDir));
            }
            catch (Exception ex)
            {
                throw new Exception($"重命名补丁目录失败：{ex.Message}", ex);
            }
        }

        public void ModifyPAKPath()
        {
            var filePath = Path.Combine(ServerDirectory, "登录器", "pak.txt");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(ServerDirectory, "登录器", $"Pak_{timestamp}.bak");
            var tempFilePath = Path.Combine(ServerDirectory, "登录器", $"Pak_temp.txt");

            try
            {
                using (StreamReader reader = new StreamReader(filePath, fileEncoding))
                using (StreamWriter writer = new StreamWriter(tempFilePath, false, fileEncoding))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var keepPart = line.Contains("data", StringComparison.OrdinalIgnoreCase) ? GetSourcePath(line, "data") : GetSourcePath(line, "Graphics");
                        var newLine = $"{PatchDirectory}\\{keepPart}";
                        writer.WriteLine();
                    }
                }
                File.Move(filePath, backupPath);
                File.Move(tempFilePath, filePath);
            }
            catch (Exception ex)
            {
                throw new Exception($"读取或写入文件时发生错误：{ex.Message}", ex);
            }
        }

        private string CaptalizeFirstLetters(string v)
        {
            var words = v.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i][1..];
                }
            }
            return string.Join("", words);
        }

        private void GetLauncherConfigInfo()
        {
            string filePath = Path.Combine(ServerDirectory, "登录器", "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            LauncherConfig = _engineType switch
            {
                EngineType.GOM => _configService.ReadMultiSectionConfig<LauncherConfigGOM>(filePath, fileEncoding),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => _configService.ReadMultiSectionConfig<LauncherConfigGEE>(filePath, fileEncoding),
                _ => throw new InvalidOperationException("不支持的引擎")
            };

            if (LauncherConfig is LauncherConfigGEE geeConfig)
            {
                geeConfig.BackgroundImage = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.BackgroundImage, "登录器"));
                geeConfig.LauncherIcon = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.LauncherIcon, "登录器"));
                geeConfig.GameCursor = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.GameCursor, "登录器"));
                geeConfig.InlayCursor = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.InlayCursor, "登录器"));
                geeConfig.DisassembleCursor = Path.Combine(ServerDirectory, GetSourcePath(geeConfig.DisassembleCursor, "登录器"));
            }
            else if (LauncherConfig is LauncherConfigGOM gomConfig)
            {
                gomConfig.BackgroundImage = Path.Combine(ServerDirectory, GetSourcePath(gomConfig.BackgroundImage, "登录器"));
            }
            else
            {
                _logger.Warning($"尝试读取未知的 LauncherConfig 类型：{LauncherConfig?.GetType().Name ?? "null"}");
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

        private void SaveLauncherConfigToFile()
        {
            var filePath = Path.Combine(ServerDirectory, "登录器", "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            if (LauncherConfig is LauncherConfigGEE geeConfig)
            {
                _configService.WriteMultiSectionConfig(filePath, geeConfig, fileEncoding);
            }
            else if (LauncherConfig is LauncherConfigGOM gomConfig)
            {
                _configService.WriteMultiSectionConfig(filePath, gomConfig, fileEncoding);
            }
            else
            {
                _logger.Warning($"尝试写入未知的 LauncherConfig 类型：{LauncherConfig?.GetType().Name ?? "null"}");
            }
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
