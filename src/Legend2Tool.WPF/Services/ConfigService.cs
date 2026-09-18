using CommunityToolkit.Mvvm.Messaging;
using IniFileParser.Model;
using Legend2Tool.WPF.Attributes;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.BackList;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.State;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        public ConfigService(
            ILogger logger,
            IEncodingService encodingService,
            IFileService fileService
        )
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
                    ("gee", EngineType.GEE),
                    ("gxx", EngineType.GXX),
                    ("hao", EngineType.LF),
                    ("v8", EngineType.V8),
                    ("blue", EngineType.BLUE),
                    ("hge", EngineType.HGE),
                    ("gamecenter", EngineType.NEWGOM),
                };
                string companyName = fileVersionInfo.CompanyName ?? string.Empty;
                string fileDescription = fileVersionInfo.FileDescription ?? string.Empty;
                foreach (var (keyword, engineType) in indicators)
                {
                    if (
                        companyName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || fileDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase)
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
                        throw new Exception(
                            apiUrl
                                + " 获取外部IP地址时发生错误。请检查网络连接或稍后重试。错误信息："
                                + ex.Message,
                            ex
                        );
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

        private static IniFileParser.IniFileParser CreateIniParser()
        {
            var parser = new IniFileParser.IniFileParser();
            parser.Parser.Configuration.AssigmentSpacer = "";
            parser.Parser.Configuration.CommentString = "#";
            parser.Parser.Configuration.SkipInvalidLines = false;
            return parser;
        }

        internal T ReadMultiSectionConfig<T>(string filePath, Encoding fileEncoding)
            where T : class, new()
        {
            IniData data = CreateIniParser().ReadFile(filePath, fileEncoding);
            T settings = ReadProperties<T>(data, filePath);
            if (settings is GEEConfig geeConfig)
            {
                geeConfig.MyGetTxtList = ReadIndexedValues(
                    data, filePath, "MyGetTxt", geeConfig.MyGetTxtNum
                );
                geeConfig.MyGetFileList = ReadIndexedValues(
                    data, filePath, "MyGetFile", geeConfig.MyGetFileNum
                );
                geeConfig.MyGetDirList = ReadIndexedValues(
                    data, filePath, "MyGetDir", geeConfig.MyGetDirNum
                );
            }
            return settings;
        }

        private T ReadProperties<T>(
            IniData data,
            string filePath,
            string? sectionOverride = null
        )
            where T : class, new()
        {
            T settings = new();
            List<string> missingKeys = [];
            foreach (var prop in typeof(T).GetProperties())
            {
                var attribute = prop.GetCustomAttribute<IniConfigAttribute>();
                if (attribute is null)
                    continue;

                string sectionName = sectionOverride ?? attribute.SectionName;
                string keyName = attribute.KeyName;
                if (!data.Sections.ContainsSection(sectionName)
                    || !data[sectionName].ContainsKey(keyName))
                {
                    missingKeys.Add($"[{sectionName}] {keyName}");
                    continue;
                }

                string? value = data[sectionName][keyName];
                if (value is null)
                    continue;

                try
                {
                    Type valueType = Nullable.GetUnderlyingType(prop.PropertyType)
                        ?? prop.PropertyType;
                    prop.SetValue(
                        settings,
                        Convert.ChangeType(value, valueType, CultureInfo.InvariantCulture)
                    );
                }
                catch (Exception ex) when (
                    ex is FormatException or OverflowException or InvalidCastException
                )
                {
                    throw new InvalidDataException(
                        $"配置文件 '{filePath}' 的 [{sectionName}] {keyName} = '{value}' 无法转换为 {prop.PropertyType.Name}。",
                        ex
                    );
                }
            }
            if (missingKeys.Count > 0)
                _logger.Warning(
                    "配置文件 {FilePath} 缺少配置项：{MissingKeys}",
                    filePath,
                    string.Join(", ", missingKeys)
                );
            return settings;
        }

        private static List<string> ReadIndexedValues(
            IniData data,
            string filePath,
            string keyPrefix,
            int count
        )
        {
            if (count < 0 || count > 10000)
                throw new InvalidDataException(
                    $"配置文件 '{filePath}' 的 [ClearServer] {keyPrefix}Num 数量无效：{count}。"
                );

            List<string> values = new(count);
            for (int i = 0; i < count; i++)
            {
                string keyName = $"{keyPrefix}{i}";
                if (!data.Sections.ContainsSection("ClearServer")
                    || !data["ClearServer"].ContainsKey(keyName)
                    || string.IsNullOrWhiteSpace(data["ClearServer"][keyName]))
                    throw new InvalidDataException(
                        $"配置文件 '{filePath}' 缺少 [ClearServer] {keyName}。"
                    );

                values.Add(data["ClearServer"][keyName]);
            }
            return values;
        }

        internal void WriteMultiSectionConfig<T>(string filePath, T config, Encoding fileEncoding)
            where T : class, new()
        {
            var parser = CreateIniParser();
            IniData data;
            if (File.Exists(filePath))
            {
                data = parser.ReadFile(filePath, fileEncoding);
            }
            else
            {
                data = new IniData();
            }

            if (config is GEEConfig countedConfig)
            {
                countedConfig.MyGetTxtNum = countedConfig.MyGetTxtList.Count;
                countedConfig.MyGetFileNum = countedConfig.MyGetFileList.Count;
                countedConfig.MyGetDirNum = countedConfig.MyGetDirList.Count;
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
                foreach (var key in data[sectionName]
                    .Select(item => item.KeyName)
                    .Where(key => IsIndexedKey(key, "MyGetTxt")
                        || IsIndexedKey(key, "MyGetFile")
                        || IsIndexedKey(key, "MyGetDir"))
                    .ToList())
                {
                    data[sectionName].RemoveKey(key);
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

        private static bool IsIndexedKey(string key, string prefix) =>
            key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(prefix.Length), out _);

        private T ReadSectionConfig<T>(
            IniData data,
            string filePath,
            string sectionName
        )
            where T : class, new()
        {
            if (!data.Sections.ContainsSection(sectionName))
                throw new InvalidDataException(
                    $"配置文件 '{filePath}' 缺少 [{sectionName}] 节。"
                );
            return ReadProperties<T>(data, filePath, sectionName);
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
            if (string.IsNullOrEmpty(configStore.M2Config.GameName))
                configStore.M2Config.GameName = "热血传奇";
            var match = Regex.Match(configStore.M2Config.GameName, pattern);
            var launcherName = match.Groups[1].Value;
            if (string.IsNullOrEmpty(launcherName))
                configStore.M2Config.GameName = "热血传奇";
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
            string newPatchDir = Path.Combine(
                Path.GetDirectoryName(configStore.PatchDirectory) ?? string.Empty,
                resourcesDir
            );
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

            var patchLists = _fileService.GetFiles(
                configStore.PatchDirectory,
                new List<string> { "*" },
                SearchOption.AllDirectories
            );

            foreach (var file in patchLists)
            {
                var fileExtension = Path.GetExtension(file).ToLowerInvariant();
                switch (fileExtension)
                {
                    case ".cache":
                    case ".pak":
                        break;
                    case ".map":
                        mapLists.Add(file);
                        break;
                    case ".wav":
                    case ".mp3":
                    case ".lrc":
                        wavLists.Add(file);
                        break;
                    default:
                        dataLists.Add(file);
                        break;
                }
            }

            var dataDirectory = Path.Combine(
                Path.GetDirectoryName(configStore.PatchDirectory)!,
                "data"
            );

            var wzlDataLists = _fileService.GetFiles(
                dataDirectory,
                new List<string> { "*" },
                SearchOption.AllDirectories
            );

            foreach (var file in wzlDataLists)
            {
                var fileExtension = Path.GetExtension(file).ToLowerInvariant();
                switch (fileExtension)
                {
                    case ".wzl":
                        wzlLists.Add(file);
                        break;
                    case ".wil":
                        wilLists.Add(file);
                        break;
                    default:
                        break;
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
            var backupPath = Path.Combine(
                configStore.ServerDirectory,
                "登录器",
                $"Pak_{timestamp}.txt"
            );
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
                        var keepPart = line.Contains("data", StringComparison.OrdinalIgnoreCase)
                            ? GetSourcePath(line, "data")
                            : GetSourcePath(line, "Graphics");
                        var newLine = $"{configStore.PatchDirectory}\\{keepPart}";
                        if (keepPart.Contains("newopui", StringComparison.OrdinalIgnoreCase))
                        {
                            if (isExists)
                                continue;
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

        public async Task SaveConfigFileAsync(ConfigStore configStore)
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
                portsToCheck = portsToCheck
                    .Concat(
                        new[]
                        {
                            geeConfig.LoginGateGatePort1,
                            geeConfig.RunGateDBPort1,
                            geeConfig.RunGateDBPort2,
                            geeConfig.RunGateDBPort3,
                            geeConfig.RunGateDBPort4,
                            geeConfig.RunGateDBPort5,
                            geeConfig.RunGateDBPort6,
                            geeConfig.RunGateDBPort7,
                            geeConfig.RunGateDBPort8,
                        }
                    )
                    .ToArray();
            }

            if (configStore.M2Config is BLUEConfig blueConfig)
            {
                portsToCheck = portsToCheck
                    .Concat(new[] { blueConfig.LoginServerMonPort })
                    .ToArray();
            }

            if (!CheckPorts(portsToCheck))
                throw new InvalidOperationException("端口检查未通过，未保存配置。");

            if (
                !string.IsNullOrEmpty(configStore.PatchDirectory)
                && (
                    configStore.EngineType != EngineType.BLUE
                    && configStore.EngineType != EngineType.HGE
                    && configStore.EngineType != EngineType.NEWGOM
                    && configStore.EngineType != EngineType.Unknown
                )
            )
            {
                RenamePatchDirectory(configStore.LauncherConfig.ResourcesDir!, configStore);
                await ModifyPAKPath(configStore);
            }
            SaveM2ConfigToFile(configStore);
            if (
                    configStore.EngineType != EngineType.BLUE
                    && configStore.EngineType != EngineType.HGE
                    && configStore.EngineType != EngineType.NEWGOM
                    && configStore.EngineType != EngineType.Unknown
            )
            {
                SaveLauncherConfigToFile(configStore);
            }
            if (configStore.AuxiliaryDefaultsPending)
            {
                SaveAuxiliaryConfigFiles(configStore);
                configStore.AuxiliaryDefaultsPending = false;
            }
        }

        public void ApplyDefaultAuxiliarySettings(ConfigStore configStore)
        {
            string serverDirectory = configStore.ServerDirectory;
            if (string.IsNullOrWhiteSpace(serverDirectory))
                throw new InvalidOperationException("请先加载服务端配置。");

            switch (configStore.M2Config)
            {
                case BLUEConfig blueConfig:
                    blueConfig.Backup = 1;
                    blueConfig.Mode = 0;
                    blueConfig.Interval = 720;
                    blueConfig.Attime = "0:0:00";
                    blueConfig.数据备份目录 = 1;
                    blueConfig.数据备份目录_path = Path.Combine(serverDirectory, "数据备份");
                    blueConfig.WinRAR目录 = 1;
                    blueConfig.WinRAR目录_path = Path.Combine(serverDirectory, "WinRAR");
                    blueConfig.FDB目录 = 1;
                    blueConfig.FDB目录_path = Path.Combine(serverDirectory, "DBServer", "FDB");
                    blueConfig.IDDB目录 = 1;
                    blueConfig.IDDB目录_path = Path.Combine(serverDirectory, "LoginSrv", "IDDB");
                    blueConfig.行会目录 = 1;
                    blueConfig.行会目录_path = Path.Combine(serverDirectory, "Mir200", "GuildBase");
                    blueConfig.沙城目录 = 1;
                    blueConfig.沙城目录_path = Path.Combine(serverDirectory, "Mir200", "Castle");
                    blueConfig.脚本数据目录 = 1;
                    blueConfig.脚本数据目录_path = Path.Combine(
                        serverDirectory, "Mir200", "Envir", "QuestDiary", "数据文件"
                    );
                    break;
                case HGEConfig hgeConfig:
                    hgeConfig.DataDir1 = serverDirectory;
                    hgeConfig.BakDir1 = Path.Combine(serverDirectory, "数据备份");
                    hgeConfig.TimeCls1 = 0;
                    hgeConfig.Hour1 = 6;
                    hgeConfig.Minute1 = 0;
                    hgeConfig.OnlyBakDatabase1 = 1;
                    hgeConfig.Count = 1;
                    hgeConfig.BakAuto = 1;
                    hgeConfig.BakReduce = 1;
                    break;
                default:
                    string setupPath = Path.Combine(serverDirectory, "Mir200", "!Setup.txt");
                    if (File.Exists(setupPath))
                    {
                        configStore.Setup.ChatDir = Path.Combine(serverDirectory, "Mir200", "ChatLog");
                        configStore.Setup.SortDir = Path.Combine(serverDirectory, "Mir200", "Sort");
                        configStore.Setup.BoxsDir = Path.Combine(serverDirectory, "Mir200", "Envir", "Boxs");
                        configStore.Setup.BoxsFile = Path.Combine(
                            serverDirectory, "Mir200", "Envir", "Boxs", "BoxsList.txt"
                        );
                    }

                    foreach (BackListBase backList in configStore.BackLists)
                    {
                        backList.Source = ConfigPathResolver.ResolveBackListPath(
                            serverDirectory, backList.Source
                        );
                        backList.Save = ConfigPathResolver.ResolveBackListPath(
                            serverDirectory, backList.Save
                        );
                        backList.Hour = 6;
                        backList.Min = 0;
                        backList.BackMode = 1;
                        backList.GetBack = 1;
                        if (backList is GEEBackList geeBackList)
                            geeBackList.IsCompress = 1;
                    }
                    configStore.AuxiliaryDefaultsPending = true;
                    break;
            }
        }

        public LoadedServerConfig LoadServerConfig(string serverDirectory)
        {
            if (string.IsNullOrWhiteSpace(serverDirectory))
                throw new ArgumentException("服务端目录不能为空", nameof(serverDirectory));

            serverDirectory = Path.GetFullPath(serverDirectory);
            string configPath = Path.Combine(serverDirectory, "config.ini");
            if (!File.Exists(configPath))
                throw new FileNotFoundException("服务端配置文件不存在", configPath);

            Encoding configEncoding = _encodingService.DetectFileEncoding(configPath);
            EngineType engineType = CheckEngineType(serverDirectory);
            M2ConfigBase m2Config = engineType switch
            {
                EngineType.GOM or EngineType.NEWGOM =>
                    ReadMultiSectionConfig<GOMConfig>(configPath, configEncoding),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8 =>
                    ReadMultiSectionConfig<GEEConfig>(configPath, configEncoding),
                EngineType.BLUE =>
                    ReadMultiSectionConfig<BLUEConfig>(configPath, configEncoding),
                EngineType.HGE =>
                    ReadMultiSectionConfig<HGEConfig>(configPath, configEncoding),
                _ => throw new InvalidOperationException("不支持的引擎"),
            };

            LauncherConfigBase launcherConfig = new();
            if (engineType is not (EngineType.BLUE or EngineType.HGE or EngineType.NEWGOM))
            {
                string launcherPath = Path.Combine(serverDirectory, "登录器", "config.ini");
                if (!File.Exists(launcherPath))
                    throw new FileNotFoundException("登录器配置文件不存在", launcherPath);

                Encoding launcherEncoding = _encodingService.DetectFileEncoding(launcherPath);
                launcherConfig = engineType switch
                {
                    EngineType.GOM =>
                        ReadMultiSectionConfig<LauncherConfigGOM>(launcherPath, launcherEncoding),
                    _ =>
                        ReadMultiSectionConfig<LauncherConfigGEE>(launcherPath, launcherEncoding),
                };
            }

            Setup setup = new();
            List<BackListBase> backLists = [];
            if (engineType is not (EngineType.BLUE or EngineType.HGE))
            {
                string setupPath = Path.Combine(serverDirectory, "Mir200", "!Setup.txt");
                if (File.Exists(setupPath))
                    setup = ReadMultiSectionConfig<Setup>(
                        setupPath,
                        _encodingService.DetectFileEncoding(setupPath)
                    );

                if (engineType != EngineType.NEWGOM)
                {
                    string backListPath = Path.Combine(serverDirectory, "BackList.txt");
                    if (File.Exists(backListPath))
                        backLists = ReadBackLists(
                            backListPath,
                            _encodingService.DetectFileEncoding(backListPath),
                            engineType
                        );
                }
            }

            return new LoadedServerConfig(
                serverDirectory,
                engineType,
                m2Config,
                launcherConfig,
                setup,
                backLists
            );
        }

        private List<BackListBase> ReadBackLists(
            string filePath,
            Encoding fileEncoding,
            EngineType engineType
        )
        {
            var parser = CreateIniParser();
            IniData data = parser.ReadFile(filePath, fileEncoding);
            List<BackListBase> backLists = [];
            foreach (var section in data.Sections)
            {
                if (!int.TryParse(section.SectionName, out _))
                    continue;

                BackListBase backList = engineType switch
                {
                    EngineType.GOM => ReadSectionConfig<BackListBase>(
                        data, filePath, section.SectionName
                    ),
                    _ => ReadSectionConfig<GEEBackList>(
                        data, filePath, section.SectionName
                    ),
                };
                backList.sectionName = section.SectionName;
                backLists.Add(backList);
            }
            return backLists;
        }

        private void SaveAuxiliaryConfigFiles(ConfigStore configStore)
        {
            string setupPath = Path.Combine(configStore.ServerDirectory, "Mir200", "!Setup.txt");
            if (File.Exists(setupPath))
                WriteMultiSectionConfig(
                    setupPath,
                    configStore.Setup,
                    _encodingService.DetectFileEncoding(setupPath)
                );

            string backListPath = Path.Combine(configStore.ServerDirectory, "BackList.txt");
            if (File.Exists(backListPath) && configStore.BackLists.Count > 0)
            {
                Encoding encoding = _encodingService.DetectFileEncoding(backListPath);
                var parser = CreateIniParser();
                IniData data = parser.ReadFile(backListPath, encoding);
                foreach (BackListBase backList in configStore.BackLists)
                {
                    if (!data.Sections.ContainsSection(backList.sectionName))
                        throw new InvalidDataException(
                            $"配置文件 '{backListPath}' 缺少 [{backList.sectionName}] 节。"
                        );

                    foreach (var prop in backList.GetType().GetProperties())
                    {
                        var attribute = prop.GetCustomAttribute<IniConfigAttribute>();
                        if (attribute is not null)
                            data[backList.sectionName][attribute.KeyName] =
                                prop.GetValue(backList)?.ToString() ?? string.Empty;
                    }
                }
                parser.WriteFile(backListPath, data, encoding);
            }
        }

        private void SaveM2ConfigToFile(ConfigStore configStore)
        {
            string filePath = Path.Combine(configStore.ServerDirectory, "config.ini");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("服务端配置文件不存在", filePath);
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
            else if (configStore.M2Config is HGEConfig hgeConfig)
            {
                WriteMultiSectionConfig(filePath, hgeConfig, fileEncoding);
            }
            else
            {
                _logger.Warning(
                    $"尝试写入未知的 M2Config 类型：{configStore.M2Config?.GetType().Name ?? "null"}"
                );
            }
        }

        private void SaveLauncherConfigToFile(ConfigStore configStore)
        {
            var filePath = Path.Combine(configStore.ServerDirectory, "登录器", "config.ini");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("登录器配置文件不存在", filePath);
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
                _logger.Warning(
                    $"尝试写入未知的 LauncherConfig 类型：{configStore.LauncherConfig?.GetType().Name ?? "null"}"
                );
            }
        }

        private string GetSourcePath(string? oldPath, string match)
        {
            try
            {
                if (string.IsNullOrEmpty(oldPath))
                    return match;
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

        public async Task GenerateCleanupScriptAsync(string baseDirectory)
        {
            var fileName = "清理文件.bat";
            var filePath = Path.Combine(baseDirectory, fileName);
            var templatePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "CleanupDirectoryTemplate.txt"
            );
            string batContent = await File.ReadAllTextAsync(
                templatePath,
                Encoding.GetEncoding("GB18030")
            );

            await File.WriteAllTextAsync(filePath, batContent, Encoding.GetEncoding("GB18030"));
        }
    }
}
