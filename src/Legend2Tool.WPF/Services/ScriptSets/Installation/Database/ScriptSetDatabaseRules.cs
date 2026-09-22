using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database
{
    internal static class ScriptSetDatabaseRules
    {
        internal const string DatabaseIndexColumn = "Idx";

        internal static void ValidateColumns(
            IEnumerable<DatabaseRowPlan> plans,
            string tableName,
            IReadOnlySet<string> tableColumns
        )
        {
            if (!tableColumns.Contains(DatabaseIndexColumn))
            {
                throw new ScriptSetInstallationException(
                    $"数据库表 {tableName} 缺少自动编号字段 {DatabaseIndexColumn}。"
                );
            }

            foreach (DatabaseRowPlan plan in plans)
            {
                string[] missingColumns = plan.Values.Keys
                    .Where(column => !tableColumns.Contains(column))
                    .ToArray();
                if (missingColumns.Length > 0)
                {
                    throw new ScriptSetInstallationException(
                        $"数据库数据“{plan.Name}”包含 {tableName} 表不存在的字段：{string.Join(", ", missingColumns)}"
                    );
                }
            }
        }

        internal static KeyValuePair<string, object?>[] GetDatabaseMatchValues(
            DatabaseRowPlan plan
        ) => plan.Values
            .Where(value => !value.Key.Equals(
                DatabaseIndexColumn,
                StringComparison.OrdinalIgnoreCase
            ))
            .ToArray();

        internal static long GetNextIndex(object? maximumValue, string tableName)
        {
            if (maximumValue is null || maximumValue is DBNull)
            {
                return 1;
            }

            try
            {
                return checked(Convert.ToInt64(maximumValue) + 1);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                throw new ScriptSetInstallationException(
                    $"无法根据 {tableName}.{DatabaseIndexColumn} 的最大值生成新编号。",
                    ex
                );
            }
        }

        internal static string GetTableName(
            EngineType engineType,
            GameDatabaseTableType tableType
        ) => (engineType, tableType) switch
        {
            (EngineType.BLUE, GameDatabaseTableType.StdItems) => "item",
            (EngineType.BLUE, GameDatabaseTableType.Monster) => "monster",
            (EngineType.BLUE, GameDatabaseTableType.Magic) => "magic",
            (_, GameDatabaseTableType.StdItems) => "StdItems",
            (_, GameDatabaseTableType.Monster) => "Monster",
            (_, GameDatabaseTableType.Magic) => "Magic",
            _ => throw new ScriptSetInstallationException(
                $"不支持的数据库表类型：{tableType}"
            )
        };
    }
}
