using Legend2Tool.WPF.Models;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Legend2Tool.WPF.Services
{
    public sealed class AppConfigService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        private readonly string _configPath;

        public AppConfigService()
            : this(Path.Combine(AppContext.BaseDirectory, "config.json"))
        {
        }

        internal AppConfigService(string configPath)
        {
            _configPath = configPath;
        }

        public AppConfig LoadOrCreate()
        {
            if (!File.Exists(_configPath))
            {
                AppConfig defaultConfig = new();
                string? directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    _configPath,
                    JsonSerializer.Serialize(defaultConfig, SerializerOptions)
                );
                return defaultConfig;
            }

            string json = File.ReadAllText(_configPath);
            AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions)
                ?? new AppConfig();
            config.DynamicMonsterSpawning = Normalize(config.DynamicMonsterSpawning);
            return config;
        }

        private static DynamicMonsterSpawningConfig Normalize(
            DynamicMonsterSpawningConfig? config
        )
        {
            DynamicMonsterSpawningConfig defaults = new();
            config ??= new DynamicMonsterSpawningConfig();
            config.FilterMapCode ??= defaults.FilterMapCode;
            config.FilterMonName ??= defaults.FilterMonName;
            config.FilterMonCount ??= defaults.FilterMonCount;
            config.FilterInterval ??= defaults.FilterInterval;
            config.FilterMonNameColor ??= defaults.FilterMonNameColor;
            config.SelectedTimeUnit ??= defaults.SelectedTimeUnit;
            config.RefreshMonTrigger ??= defaults.RefreshMonTrigger;
            config.ClearMonTrigger ??= defaults.ClearMonTrigger;
            return config;
        }
    }
}
