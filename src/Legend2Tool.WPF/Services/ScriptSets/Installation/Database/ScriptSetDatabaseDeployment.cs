using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.ServerConfiguration;
using System.IO;
using System.Text.Json;
using BlueConfig = Legend2Tool.WPF.Models.M2Config.M2Config.BLUEConfig;
using GeeConfig = Legend2Tool.WPF.Models.M2Config.M2Config.GEEConfig;
using GomConfig = Legend2Tool.WPF.Models.M2Config.M2Config.GOMConfig;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database
{
    internal static class ScriptSetDatabaseDeployment
    {
        internal static List<DatabaseRowPlan> CreateDatabasePlans(
            IReadOnlyList<ScriptSetDatabaseDataInfo> databaseRows
        )
        {
            var plans = new List<DatabaseRowPlan>(databaseRows.Count);
            foreach (ScriptSetDatabaseDataInfo databaseRow in databaseRows)
            {
                Dictionary<string, object?> values = ParseDatabaseValues(
                    databaseRow.DataJson,
                    databaseRow.Name
                );
                plans.Add(new DatabaseRowPlan(databaseRow.TableType, databaseRow.Name, values));
            }
            return plans;
        }

        internal static Dictionary<string, object?> ParseDatabaseValues(
            string dataJson,
            string displayName
        )
        {
            if (string.IsNullOrWhiteSpace(dataJson))
            {
                throw new ScriptSetInstallationException(
                    $"数据库数据“{displayName}”没有可追加的内容。"
                );
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(dataJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ScriptSetInstallationException(
                        $"数据库数据“{displayName}”必须是 JSON 对象。"
                    );
                }

                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    string columnName = property.Name.Trim();
                    if (columnName.Length == 0 || !values.TryAdd(
                        columnName,
                        ConvertJsonValue(property.Value)
                    ))
                    {
                        throw new ScriptSetInstallationException(
                            $"数据库数据“{displayName}”包含空列名或重复列名。"
                        );
                    }
                }

                if (values.Count == 0)
                {
                    throw new ScriptSetInstallationException(
                        $"数据库数据“{displayName}”没有可追加的字段。"
                    );
                }
                return values;
            }
            catch (JsonException ex)
            {
                throw new ScriptSetInstallationException(
                    $"数据库数据“{displayName}”不是有效的 JSON。",
                    ex
                );
            }
        }

        private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };

        internal static DatabaseTarget ResolveDatabaseTarget(
            string serverDirectory,
            EngineType engineType,
            M2ConfigBase m2Config
        )
        {
            (string? configuredPath, DatabaseProvider provider) = engineType switch
            {
                EngineType.BLUE when m2Config is BlueConfig config =>
                    (config.DataTableFile, DatabaseProvider.Sqlite),
                EngineType.GOM or EngineType.NEWGOM
                    when m2Config is GomConfig config =>
                    (config.AccessFileName, DatabaseProvider.Access),
                EngineType.HGE when m2Config is HGEConfig config =>
                    (config.SQLiteName, DatabaseProvider.Sqlite),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8
                    when m2Config is GeeConfig config =>
                    (config.SqliteDBName, DatabaseProvider.Sqlite),
                _ => throw new ScriptSetInstallationException(
                    $"当前引擎不支持数据库追加：{engineType}"
                )
            };

            string databasePath = ConfigPathResolver.ResolveServerPath(
                serverDirectory,
                configuredPath,
                "Mud2"
            );
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                throw new ScriptSetInstallationException(
                    $"没有找到当前引擎的数据库文件：{databasePath}"
                );
            }
            return new DatabaseTarget(databasePath, provider, engineType);
        }

        internal static void ValidateDatabasePlans(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans
        ) => GetDatabase(target.Provider).Validate(target, plans);

        internal static void InsertDatabaseRows(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        ) => GetDatabase(target.Provider).Insert(target, plans, cancellationToken);

        internal static int RemoveDatabaseRows(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        ) => GetDatabase(target.Provider).Remove(target, plans, cancellationToken);

        internal static void ValidateDatabaseRemovalPlans(
            IEnumerable<DatabaseRowPlan> plans
        )
        {
            DatabaseRowPlan? unsafePlan = plans.FirstOrDefault(
                plan => !plan.Values.Keys.Any(
                    column => !column.Equals(
                        ScriptSetDatabaseRules.DatabaseIndexColumn,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
            if (unsafePlan is not null)
            {
                throw new ScriptSetInstallationException(
                    $"数据库数据“{unsafePlan.Name}”除 {ScriptSetDatabaseRules.DatabaseIndexColumn} 外没有可用于安全匹配的字段，已停止删除。"
                );
            }
        }

        private static IScriptSetDatabase GetDatabase(DatabaseProvider provider) => provider == DatabaseProvider.Access
            ? new AccessScriptSetDatabase()
            : new SqliteScriptSetDatabase();
    }
}
