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
                Save(defaultConfig);
                return defaultConfig;
            }

            string json = File.ReadAllText(_configPath);
            AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions)
                ?? new AppConfig();
            config.DynamicMonsterSpawning = Normalize(config.DynamicMonsterSpawning);
            return config;
        }

        public void Save(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            string fullConfigPath = Path.GetFullPath(_configPath);
            string directory = Path.GetDirectoryName(fullConfigPath)!;
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullConfigPath)}.{Guid.NewGuid():N}.tmp"
            );

            try
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(config, SerializerOptions);
                using (
                    var stream = new FileStream(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.WriteThrough
                    )
                )
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, fullConfigPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
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
