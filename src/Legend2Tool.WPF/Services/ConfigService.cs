using IniFileParser.Model;
using Legend2Tool.WPF.Attributes;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Services
{
    public class ConfigService : IConfigService
    {
        private readonly ILogger _logger;

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

        public ConfigService(ILogger logger)
        {
            _logger = logger;
        }

        public EngineType CheckEngineType(string serverDirectory)
        {

            string primaryPath = Path.Combine(serverDirectory, "GameOfMir引擎控制器.exe");
            string filePath = File.Exists(primaryPath)
                ? primaryPath
                : Path.Combine(serverDirectory, "GameCenter.exe");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The specified file does not exist.", filePath);
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


        public T ReadMultiSectionConfig<T>(string filePath, Encoding fileEncoding) where T : class, new()
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

        public void WriteMultiSectionConfig<T>(string filePath, T config, Encoding fileEncoding) where T : class, new()
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

        public T ReadSectionConfig<T>(string filePath, Encoding fileEncoding, string sectionName) where T : class, new()
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

        public void WriteSectionConfig<T>(string filePath, T config, Encoding fileEncoding, string sectionName) where T : class, new()
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

    }
}
