using IniFileParser.Model;
using Legend2Tool.WPF.Attributes;
using Legend2Tool.WPF.Enums;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Legend2Tool.WPF.Services
{
    public class ConfigService : IConfigService
    {
        private readonly ILogger _logger;

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
                _logger.Error($"文件路径不存在：{filePath}");
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

        public T ReadMultiSectionConfig<T>(string filePath, Encoding fileEncoding) where T : class, new()
        {
            var parser = new IniFileParser.IniFileParser();
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
                                _logger.Error($"转换属性{prop.Name}(节：{sectionName},键：{keyName}:{ex.Message})");
                            }
                        }
                    }
                    else
                    {
                        _logger.Warning($"在 INI 文件 {filePath} 中未找到节'{sectionName}'");
                    }
                }
            }
            return settings;
        }
    }
}
