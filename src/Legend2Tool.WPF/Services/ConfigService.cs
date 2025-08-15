using CommunityToolkit.Mvvm.Messaging;
using IniFileParser.Model;
using Legend2Tool.WPF.Attributes;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.BackList;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.State;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using TinyPinyin;

namespace Legend2Tool.WPF.Services
{
    public class ConfigService : IConfigService
    {
        private readonly ILogger _logger;
        private readonly IEncodingService _encodingService;
        private readonly IFileService _fileService;

        private readonly List<string> _apiUrls =
        [
            "https://ipinfo.io/ip",
            "https://api64.ipify.org",
            "https://ipecho.net/plain",
            "https://checkip.amazonaws.com",
            "https://ident.me",
            "https://wtfismyip.com/text",
            "http://ip-api.com/line/?fields=query",
            "https://ipaddress.sh/",
            "https://myexternalip.com/raw",
        ];

        public ConfigService(ILogger logger, IEncodingService encodingService, IFileService fileService)
        {
            _logger = logger;
            _encodingService = encodingService;
            _fileService = fileService;
        }

        public EngineType CheckEngineType(string serverDirectory)
        {

            string primaryPath = Path.Combine(serverDirectory, "GameOfMir引擎控制器.exe");
            string filePath = File.Exists(primaryPath)
                ? primaryPath
                : Path.Combine(serverDirectory, "GameCenter.exe");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("指定的文件不存在", filePath);
            }
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(filePath);

            if (fileVersionInfo != null)
            {
                var indicators = new (string Keyword, EngineType EngineType)[]
                {
                     ("gameofmir", EngineType.GOM),
                     ("gamecenter", EngineType.NEWGOM),
                     ("gee", EngineType.GEE),
                     ("gxx", EngineType.GXX),
                     ("hao", EngineType.LF),
                     ("v8", EngineType.V8),
                     ("blue", EngineType.BLUE),
                };
                string companyName = fileVersionInfo.CompanyName ?? string.Empty;
                string fileDescription = fileVersionInfo.FileDescription ?? string.Empty;
                foreach (var (keyword, engineType) in indicators)
                {
                    if (
                    companyName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        fileDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return engineType;
                    }
                }
            }
            return EngineType.Unknown;
        }

        public async Task<string> GetExternalIpAddressAsync()
        {
            string ip = await GetIpAddressAsync();
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("无法获取外部IP地址");
                return string.Empty;
            }
            return ip;
        }

        private async Task<string> GetIpAddressAsync()
        {
            Random random = new Random();
            var shuffledApiUrls = _apiUrls.OrderBy(x => random.Next()).ToList();

            foreach (var apiUrl in shuffledApiUrls)
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    try
                    {
                        string ip = await GetIpAddressFromApiAsync(apiUrl, cts.Token);
                        if (!string.IsNullOrEmpty(ip))
                        {
                            return ip;
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        throw new OperationCanceledException(
                                                $"获取外部IP地址时请求超时或被取消。请检查网络连接或稍后重试。错误信息：{ex.Message}",
                                                ex
                                            );
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(apiUrl + " 获取外部IP地址时发生错误。请检查网络连接或稍后重试。错误信息：" + ex.Message, ex);
                    }
                }
            }
            return string.Empty;
        }

        private async Task<string> GetIpAddressFromApiAsync(string apiUrl, CancellationToken token)
        {
            using HttpClient client = new HttpClient();
            try
            {
                HttpResponseMessage response = await client.GetAsync(apiUrl, token);
                response.EnsureSuccessStatusCode();
                string ip = await response.Content.ReadAsStringAsync(token);
                return ip.Trim();
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException(
                    $"无法从 API '{apiUrl}' 获取外部 IP 地址。请检查网络连接或稍后重试。错误信息：{ex.Message}",
                    ex
                );
            }
        }

        private T ReadMultiSectionConfig<T>(string filePath, Encoding fileEncoding) where T : class, new()
        {
            var parser = new IniFileParser.IniFileParser();
            parser.Parser.Configuration.AssigmentSpacer = ""; // <-- THIS IS THE KEY FIX
            parser.Parser.Configuration.CommentString = "#"; // Good practice
            parser.Parser.Configuration.SkipInvalidLines = true; // Good practice
            IniData data = parser.ReadFile(filePath, fileEncoding);

            T settings = new T();
            var properties = typeof(T).GetProperties();

            foreach (var prop in properties)
            {
                var iniConfigAttribute = prop.GetCustomAttribute<IniConfigAttribute>();
                if (iniConfigAttribute != null)
                {
                    string sectionName = iniConfigAttribute.SectionName;
                    string keyName = iniConfigAttribute.KeyName;

                    if (data.Sections.ContainsSection(sectionName))
                    {
                        string value = data[sectionName][keyName];
                        if (value != null)
                        {
                            try
                            {
                                prop.SetValue(settings, Convert.ChangeType(value, prop.PropertyType));
                            }
                            catch (InvalidCastException ex)
                            {
                                throw new InvalidOperationException(
                                    $"无法将值 '{value}' 转换为属性 '{prop.Name}' 的类型 '{prop.PropertyType.Name}'。",
                                    ex
                                );
                            }
                        }
                    }
                    else
                    {
                        _logger.Warning($"在 INI 文件 {filePath} 中未找到节'{sectionName}'");
                    }
                }
            }

            if (settings is GEEConfig geeConfig)
            {
                geeConfig.MyGetTxtList.Clear();
                var sectionName = "ClearServer";
                for (int i = 0; i < geeConfig.MyGetTxtNum; i++)
                {
                    string keyName = $"MyGetTxt{i}";
                    if (data.Sections.ContainsSection(sectionName) && data[sectionName].ContainsKey(keyName))
                    {
                        string? value = data[sectionName][keyName];
                        if (!string.IsNullOrEmpty(value))
                        {
                            geeConfig.MyGetTxtList.Add(value);
                        }
                    }
                }
                for (int i = 0; i < geeConfig.MyGetFileNum; i++)
                {
                    string keyName = $"MyGetFile{i}";
                    if (data.Sections.ContainsSection(sectionName) && data[sectionName].ContainsKey(keyName))
                    {
                        string? value = data[sectionName][keyName];
                        if (!string.IsNullOrEmpty(value))
                        {
                            geeConfig.MyGetFileList.Add(value);
                        }
                    }
                }
                for (int i = 0; i < geeConfig.MyGetDirNum; i++)
                {
                    string keyName = $"MyGetDir{i}";
                    if (data.Sections.ContainsSection(sectionName) && data[sectionName].ContainsKey(keyName))
                    {
                        string? value = data[sectionName][keyName];
                        if (!string.IsNullOrEmpty(value))
                        {
                            geeConfig.MyGetDirList.Add(value);
                        }
                    }
                }
            }

            return settings;
        }

        private void WriteMultiSectionConfig<T>(string filePath, T config, Encoding fileEncoding) where T : class, new()
        {
            var parser = new IniFileParser.IniFileParser();
            parser.Parser.Configuration.AssigmentSpacer = ""; // <-- THIS IS THE KEY FIX
            parser.Parser.Configuration.CommentString = "#"; // Good practice
            parser.Parser.Configuration.SkipInvalidLines = true; // Good practice
            IniData data;
            if (File.Exists(filePath))
            {
                data = parser.ReadFile(filePath, fileEncoding);
            }
            else
            {
                data = new IniData();
            }

            var properties = typeof(T).GetProperties();

            foreach (var prop in properties)
            {
                var iniConfigAttribute = prop.GetCustomAttribute<IniConfigAttribute>();
                if (iniConfigAttribute != null)
                {
                    string sectionName = iniConfigAttribute.SectionName;
                    string keyName = iniConfigAttribute.KeyName;
                    object? value = prop.GetValue(config);
                    if (!data.Sections.ContainsSection(sectionName))
                    {
                        data.Sections.AddSection(sectionName);
                    }
                    data[sectionName][keyName] = value?.ToString() ?? string.Empty;
                }
            }

            if (config is GEEConfig geeConfig)
            {
                var sectionName = "ClearServer";
                if (!data.Sections.ContainsSection(sectionName))
                {
                    data.Sections.AddSection(sectionName);
                }
                for (int i = 0; i < geeConfig.MyGetTxtList.Count; i++)
                {
                    string keyName = $"MyGetTxt{i}";
                    data[sectionName][keyName] = geeConfig.MyGetTxtList[i];
                }
                for (int i = 0; i < geeConfig.MyGetFileList.Count; i++)
                {
                    string keyName = $"MyGetFile{i}";
                    data[sectionName][keyName] = geeConfig.MyGetFileList[i];
                }
                for (int i = 0; i < geeConfig.MyGetDirList.Count; i++)
                {
                    string keyName = $"MyGetDir{i}";
                    data[sectionName][keyName] = geeConfig.MyGetDirList[i];
                }
            }
            parser.WriteFile(filePath, data, fileEncoding);
        }

        private T ReadSectionConfig<T>(string filePath, Encoding fileEncoding, string sectionName) where T : class, new()
        {

            var parser = new IniFileParser.IniFileParser();
            parser.Parser.Configuration.AssigmentSpacer = ""; // <-- THIS IS THE KEY FIX
            parser.Parser.Configuration.CommentString = "#"; // Good practice
            parser.Parser.Configuration.SkipInvalidLines = true; // Good practice
            IniData data = parser.ReadFile(filePath, fileEncoding);
            if (data.Sections.ContainsSection(sectionName))
            {
                T settings = new T();
                var properties = typeof(T).GetProperties();

                foreach (var prop in properties)
                {
                    var iniConfigAttribute = prop.GetCustomAttribute<IniConfigAttribute>();
                    if (iniConfigAttribute != null)
                    {
                        string keyName = iniConfigAttribute.KeyName;

                        string? value = data[sectionName][keyName];
                        if (value != null)
                        {
                            try
                            {
                                prop.SetValue(settings, Convert.ChangeType(value, prop.PropertyType));
                            }
                            catch (InvalidCastException ex)
                            {
                                throw new InvalidOperationException(
                                    $"无法将值 '{value}' 转换为属性 '{prop.Name}' 的类型 '{prop.PropertyType.Name}'。",
                                    ex
                                );
                            }
                        }
                    }
                }
                return settings;
            }
            else
            {
                return null!;
            }
        }

        private void WriteSectionConfig<T>(string filePath, T config, Encoding fileEncoding, string sectionName) where T : class, new()
        {
            var parser = new IniFileParser.IniFileParser();
            parser.Parser.Configuration.AssigmentSpacer = ""; // <-- THIS IS THE KEY FIX
            parser.Parser.Configuration.CommentString = "#"; // Good practice
            parser.Parser.Configuration.SkipInvalidLines = true; // Good practice
            IniData data;
            if (File.Exists(filePath))
            {
                data = parser.ReadFile(filePath, fileEncoding);
            }
            else
            {
                data = new IniData();
            }

            var properties = typeof(T).GetProperties();

            foreach (var prop in properties)
            {
                var iniConfigAttribute = prop.GetCustomAttribute<IniConfigAttribute>();
                if (iniConfigAttribute != null)
                {
                    string KeyName = iniConfigAttribute.KeyName;
                    object? value = prop.GetValue(config);

                    if (!data.Sections.ContainsSection(sectionName))
                    {
                        data.Sections.AddSection(sectionName);
                    }
                    data[sectionName][KeyName] = value?.ToString() ?? string.Empty;
                }
            }
            parser.WriteFile(filePath, data, fileEncoding);
        }

        public bool CheckPorts(int[] portsToCheck)
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

        public string GetLauncherName(ConfigStore configStore)
        {
            string pattern = @"^(.*?)[\d一二三四五六七八九十]+区";
            if (string.IsNullOrEmpty(configStore.M2Config.GameName)) configStore.M2Config.GameName = "热血传奇";
            var match = Regex.Match(configStore.M2Config.GameName, pattern);
            var launcherName = match.Groups[1].Value;
            if (string.IsNullOrEmpty(launcherName)) configStore.M2Config.GameName = "热血传奇";
            return launcherName;
        }

        private void RenamePatchDirectory(string resourcesDir, ConfigStore configStore)
        {
            if (string.IsNullOrEmpty(configStore.PatchDirectory))
            {
                MessageBox.Show("补丁目录未设置");
                return;
            }
            if (!Directory.Exists(configStore.PatchDirectory))
            {
                MessageBox.Show($"补丁目录不存在：{configStore.PatchDirectory}");
                return;
            }
            string newPatchDir = Path.Combine(Path.GetDirectoryName(configStore.PatchDirectory) ?? string.Empty, resourcesDir);
            if (newPatchDir == configStore.PatchDirectory)
            {
                MessageBox.Show("补丁目录未更改");
                return;
            }
            try
            {
                Directory.Move(configStore.PatchDirectory, newPatchDir);
                configStore.PatchDirectory = newPatchDir;
                WeakReferenceMessenger.Default.Send(new PatchDirectoryChangedMessage(newPatchDir));
            }
            catch (Exception ex)
            {
                throw new Exception($"重命名补丁目录失败：{ex.Message}", ex);
            }
        }

        private async Task ModifyPAKPath(ConfigStore configStore)
        {
            var mapPath = Path.Combine(configStore.ServerDirectory, "登录器", "map.txt");
            var dataPath = Path.Combine(configStore.ServerDirectory, "登录器", "data.txt");
            var wavPath = Path.Combine(configStore.ServerDirectory, "登录器", "wav.txt");
            var wzlPath = Path.Combine(configStore.ServerDirectory, "登录器", "wzl.txt");
            var wilPath = Path.Combine(configStore.ServerDirectory, "登录器", "wil.txt");

            var dataLists = new List<string>();
            var mapLists = new List<string>();
            var wavLists = new List<string>();
            var wzlLists = new List<string>();
            var wilLists = new List<string>();

            var patchLists = _fileService.GetFiles(configStore.PatchDirectory, new List<string>
            {
                "*",
            }, SearchOption.AllDirectories);

            foreach (var file in patchLists)
            {
                var fileExtension = Path.GetExtension(file).ToLowerInvariant();
                switch (fileExtension)
                {
                    case ".cache": case ".pak": break;
                    case ".map": mapLists.Add(file); break;
                    case ".wav": case ".mp3": case ".lrc": wavLists.Add(file); break;
                    default: dataLists.Add(file); break;
                }
            }

            var dataDirectory = Path.Combine(Path.GetDirectoryName(configStore.PatchDirectory)!, "data");

            var wzlDataLists = _fileService.GetFiles(dataDirectory, new List<string>
            {
                "*",
            }, SearchOption.AllDirectories);

            foreach (var file in wzlDataLists)
            {
                var fileExtension = Path.GetExtension(file).ToLowerInvariant();
                switch (fileExtension)
                {
                    case ".wzl": wzlLists.Add(file); break;
                    case ".wil": wilLists.Add(file); break;
                    default: break;
                }
            }

            await File.WriteAllLinesAsync(mapPath, mapLists, Encoding.GetEncoding("GB18030"));
            await File.WriteAllLinesAsync(dataPath, dataLists, Encoding.GetEncoding("GB18030"));
            await File.WriteAllLinesAsync(wavPath, wavLists, Encoding.GetEncoding("GB18030"));
            await File.WriteAllLinesAsync(wzlPath, wzlLists, Encoding.GetEncoding("GB18030"));
            await File.WriteAllLinesAsync(wilPath, wilLists, Encoding.GetEncoding("GB18030"));

            var pakPath = Path.Combine(configStore.ServerDirectory, "登录器", "pak.txt");
            if (!File.Exists(pakPath))
            {
                MessageBox.Show($"文件不存在：{pakPath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(pakPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(configStore.ServerDirectory, "登录器", $"Pak_{timestamp}.txt");
            var tempFilePath = Path.Combine(configStore.ServerDirectory, "登录器", $"Pak_temp.txt");
            bool isExists = false;

            try
            {
                using (StreamReader reader = new StreamReader(pakPath, fileEncoding))
                using (StreamWriter writer = new StreamWriter(tempFilePath, false, fileEncoding))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var keepPart = line.Contains("data", StringComparison.OrdinalIgnoreCase) ? GetSourcePath(line, "data") : GetSourcePath(line, "Graphics");
                        var newLine = $"{configStore.PatchDirectory}\\{keepPart}";
                        if (keepPart.Contains("newopui", StringComparison.OrdinalIgnoreCase))
                        {
                            if (isExists) continue;
                            keepPart = GetSourcePath(line, "newopui.pak");
                            newLine = $"{configStore.ServerDirectory}\\登录器\\{keepPart}";
                            isExists = true;
                        }
                        writer.WriteLine(newLine);
                    }
                }
                File.Move(pakPath, backupPath);
                File.Move(tempFilePath, pakPath);
            }
            catch (Exception ex)
            {
                throw new Exception($"读取或写入文件时发生错误：{ex.Message}", ex);
            }
        }

        public void SaveConfigFile(ConfigStore configStore)
        {
            int[] portsToCheck =
            [
                configStore.M2Config.DBServerGatePort,
                configStore.M2Config.DBServerServerPort,
                configStore.M2Config.M2ServerGatePort,
                configStore.M2Config.M2ServerMsgSrvPort,
                configStore.M2Config.RunGateGatePort1,
                configStore.M2Config.LoginGateGatePort,
                configStore.M2Config.SelGateGatePort,
                configStore.M2Config.LoginServerGatePort,
                //configStore.M2Config.LoginServerServerPort,
                configStore.M2Config.LogServerPort,
            ];

            if (configStore.M2Config is GEEConfig geeConfig)
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

            if (configStore.M2Config is BLUEConfig blueConfig)
            {
                portsToCheck = portsToCheck.Concat(new[] { blueConfig.LoginServerMonPort }).ToArray();
            }

            if (!CheckPorts(portsToCheck))
            {
                return;
            }

            RenamePatchDirectory(configStore.LauncherConfig.ResourcesDir!, configStore);
            ModifyPAKPath(configStore);
            SaveM2ConfigToFile(configStore);
            SaveLauncherConfigToFile(configStore);
        }

        public void GetLauncherConfigInfo(ConfigStore configStore)
        {
            string filePath = Path.Combine(configStore.ServerDirectory, "登录器", "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            configStore.LauncherConfig = configStore.EngineType switch
            {
                EngineType.GOM => ReadMultiSectionConfig<LauncherConfigGOM>(filePath, fileEncoding),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => ReadMultiSectionConfig<LauncherConfigGEE>(filePath, fileEncoding),
                _ => ReadMultiSectionConfig<LauncherConfigBase>(filePath, fileEncoding)
            };

            if (configStore.LauncherConfig is LauncherConfigGEE geeConfig)
            {
                geeConfig.BackgroundImage = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.BackgroundImage, "登录器"));
                geeConfig.LauncherIcon = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.LauncherIcon, "登录器"));
                geeConfig.GameCursor = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.GameCursor, "登录器"));
                geeConfig.InlayCursor = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.InlayCursor, "登录器"));
                geeConfig.DisassembleCursor = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.DisassembleCursor, "登录器"));
            }
            else if (configStore.LauncherConfig is LauncherConfigGOM gomConfig)
            {
                gomConfig.BackgroundImage = Path.Combine(configStore.ServerDirectory, GetSourcePath(gomConfig.BackgroundImage, "登录器"));
            }
            else
            {
                _logger.Warning($"尝试读取未知的 LauncherConfig 类型：{configStore.LauncherConfig?.GetType().Name ?? "null"}");
            }
        }

        public void GetM2ConfigInfo(ConfigStore configStore)
        {
            string filePath = Path.Combine(configStore.ServerDirectory, "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            configStore.EngineType = CheckEngineType(configStore.ServerDirectory);
            configStore.M2Config = configStore.EngineType switch
            {
                EngineType.GOM or EngineType.NEWGOM => ReadMultiSectionConfig<GOMConfig>(filePath, fileEncoding),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => ReadMultiSectionConfig<GEEConfig>(filePath, fileEncoding),
                EngineType.BLUE => ReadMultiSectionConfig<BLUEConfig>(filePath, fileEncoding),
                _ => throw new InvalidOperationException("不支持的引擎")
            };

            configStore.M2Config.GameDirectory = $"{configStore.ServerDirectory}\\";

            if (configStore.M2Config is GEEConfig geeConfig)
            {
                geeConfig.SqliteDBFile = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.SqliteDBFile, "Mud2"));
                geeConfig.SqliteDBName = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.SqliteDBName, "Mud2"));
                if (geeConfig.MyGetTxtNum > 0)
                {
                    for (int i = 0; i < geeConfig.MyGetTxtNum; i++)
                    {
                        geeConfig.MyGetTxtList[i] = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.MyGetTxtList[i], "Mir200"));
                    }
                }
                if (geeConfig.MyGetFileNum > 0)
                {
                    for (int i = 0; i < geeConfig.MyGetFileNum; i++)
                    {
                        geeConfig.MyGetFileList[i] = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.MyGetFileList[i], "Mir200"));
                    }
                }
                if (geeConfig.MyGetDirNum > 0)
                {
                    for (int i = 0; i < geeConfig.MyGetDirNum; i++)
                    {
                        geeConfig.MyGetDirList[i] = Path.Combine(configStore.ServerDirectory, GetSourcePath(geeConfig.MyGetDirList[i], "Mir200"));
                    }
                }

                SetDefaultBackListPath(configStore);
                SetDefaultSetupPath(configStore);
            }
            else if (configStore.M2Config is GOMConfig gomConfig)
            {
                gomConfig.AccessFileName = Path.Combine(configStore.ServerDirectory, GetSourcePath(gomConfig.AccessFileName, "Mud2"));
                if (configStore.EngineType == EngineType.GOM)
                    SetDefaultBackListPath(configStore);
                SetDefaultSetupPath(configStore);
            }
            else if (configStore.M2Config is BLUEConfig blueConfig)
            {
                blueConfig.DataTableFile = Path.Combine(configStore.ServerDirectory, GetSourcePath(blueConfig.DataTableFile, "Mud2"));
                blueConfig.Backup = 1;
                blueConfig.Mode = 0;
                blueConfig.Interval = 720;
                blueConfig.Attime = "0:0:00";
                blueConfig.数据备份目录 = 1;
                blueConfig.数据备份目录_path = Path.Combine(configStore.ServerDirectory, "数据备份");
                blueConfig.WinRAR目录 = 1;
                blueConfig.WinRAR目录_path = Path.Combine(configStore.ServerDirectory, "WinRAR");
                blueConfig.FDB目录 = 1;
                blueConfig.FDB目录_path = Path.Combine(configStore.ServerDirectory, "DBServer", "FDB");
                blueConfig.IDDB目录 = 1;
                blueConfig.IDDB目录_path = Path.Combine(configStore.ServerDirectory, "LoginSrv", "IDDB");
                blueConfig.行会目录 = 1;
                blueConfig.行会目录_path = Path.Combine(configStore.ServerDirectory, "Mir200", "GuildBase");
                blueConfig.沙城目录 = 1;
                blueConfig.沙城目录_path = Path.Combine(configStore.ServerDirectory, "Mir200", "Castle");
                blueConfig.脚本数据目录 = 1;
                blueConfig.脚本数据目录_path = Path.Combine(configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "数据文件");
            }
            else
            {
                _logger.Warning($"尝试读取未知的 M2Config 类型：{configStore.M2Config?.GetType().Name ?? "null"}");
            }

        }

        private void SetDefaultSetupPath(ConfigStore configStore)
        {
            var filePath = Path.Combine(configStore.ServerDirectory, "Mir200", "!Setup.txt");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);

            configStore.Setup = ReadMultiSectionConfig<Setup>(filePath, fileEncoding);

            configStore.Setup.ChatDir = Path.Combine(configStore.ServerDirectory, "Mir200", "ChatLog");
            configStore.Setup.SortDir = Path.Combine(configStore.ServerDirectory, "Mir200", "Sort");
            configStore.Setup.BoxsDir = Path.Combine(configStore.ServerDirectory, "Mir200", "Envir", "Boxs");
            configStore.Setup.BoxsFile = Path.Combine(configStore.ServerDirectory, "Mir200", "Envir", "Boxs", "BoxsList.txt");

            WriteMultiSectionConfig(filePath, configStore.Setup, fileEncoding);
        }

        private void SetDefaultBackListPath(ConfigStore configStore)
        {
            string filePath = Path.Combine(configStore.ServerDirectory, "BackList.txt");
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

            configStore.BackLists.Clear();


            foreach (var section in data.Sections)
            {
                if (int.TryParse(section.SectionName, out _))
                {
                    var backList = new BackListBase();
                    backList = configStore.EngineType switch
                    {
                        EngineType.GOM => ReadSectionConfig<BackListBase>(filePath, fileEncoding, section.SectionName),
                        EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 => ReadSectionConfig<GEEBackList>(filePath, fileEncoding, section.SectionName),
                        _ => ReadSectionConfig<BackListBase>(filePath, fileEncoding, section.SectionName)
                    };
                    backList.sectionName = section.SectionName;
                    configStore.BackLists.Add(backList);
                }
            }

            foreach (var backList in configStore.BackLists)
            {
                backList.Source = Path.Combine(configStore.ServerDirectory, GetSourcePath(backList.Source, "mirserver")[10..]);
                backList.Save = Path.Combine(configStore.ServerDirectory, GetSourcePath(backList.Save, "mirserver")[10..]);
                backList.Hour = 6;
                backList.Min = 0;
                backList.BackMode = 1;
                backList.GetBack = 1;
                if (backList is GEEBackList geeBackList)
                {
                    geeBackList.IsCompress = 1;
                    WriteSectionConfig<GEEBackList>(filePath, geeBackList, fileEncoding, backList.sectionName);
                    continue;
                }
                WriteSectionConfig<BackListBase>(filePath, backList, fileEncoding, backList.sectionName);
            }
        }

        private void SaveM2ConfigToFile(ConfigStore configStore)
        {
            string filePath = Path.Combine(configStore.ServerDirectory, "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);


            if (configStore.M2Config is GEEConfig geeConfig)
            {
                WriteMultiSectionConfig(filePath, geeConfig, fileEncoding);
            }
            else if (configStore.M2Config is GOMConfig gomConfig)
            {
                WriteMultiSectionConfig(filePath, gomConfig, fileEncoding);
            }
            else if (configStore.M2Config is BLUEConfig blueConfig)
            {
                WriteMultiSectionConfig(filePath, blueConfig, fileEncoding);
            }
            else
            {
                _logger.Warning($"尝试写入未知的 M2Config 类型：{configStore.M2Config?.GetType().Name ?? "null"}");
            }
        }

        private void SaveLauncherConfigToFile(ConfigStore configStore)
        {
            var filePath = Path.Combine(configStore.ServerDirectory, "登录器", "config.ini");
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"文件不存在：{filePath}");
                return;
            }
            var fileEncoding = _encodingService.DetectFileEncoding(filePath);
            if (configStore.LauncherConfig is LauncherConfigGEE geeConfig)
            {
                WriteMultiSectionConfig(filePath, geeConfig, fileEncoding);
            }
            else if (configStore.LauncherConfig is LauncherConfigGOM gomConfig)
            {
                WriteMultiSectionConfig(filePath, gomConfig, fileEncoding);
            }
            else
            {
                _logger.Warning($"尝试写入未知的 LauncherConfig 类型：{configStore.LauncherConfig?.GetType().Name ?? "null"}");
            }
        }
        private string GetSourcePath(string? oldPath, string match)
        {
            try
            {
                if (string.IsNullOrEmpty(oldPath)) return match;
                int startIndex = oldPath!.IndexOf(match, StringComparison.OrdinalIgnoreCase);
                if (startIndex != -1)
                {
                    return oldPath[startIndex..];
                }
                return oldPath ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new Exception($"处理路径时发生错误：{ex.Message}", ex);
            }
        }
    }
}
