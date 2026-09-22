using Legend2Tool.WPF.Enums;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database
{
    internal sealed class SqliteScriptSetDatabase : IScriptSetDatabase
    {
        public void Validate(DatabaseTarget target, IReadOnlyList<DatabaseRowPlan> plans)
        {
            Batteries_V2.Init();
            using var connection = new SqliteConnection(CreateSqliteConnectionString(target.Path));
            connection.Open();
            ValidateSqlitePlans(connection, target.EngineType, plans);
        }

        private static void ValidateSqlitePlans(
            SqliteConnection connection,
            EngineType engineType,
            IReadOnlyList<DatabaseRowPlan> plans
        )
        {
            foreach (IGrouping<string, DatabaseRowPlan> group in plans.GroupBy(
                plan => ScriptSetDatabaseRules.GetTableName(engineType, plan.TableType),
                StringComparer.OrdinalIgnoreCase
            ))
            {
                HashSet<string> columns = GetSqliteColumns(connection, group.Key);
                ScriptSetDatabaseRules.ValidateColumns(group, group.Key, columns);
            }
        }

        private static HashSet<string> GetSqliteColumns(
            SqliteConnection connection,
            string tableName
        )
        {
            using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name COLLATE NOCASE;";
            existsCommand.Parameters.AddWithValue("$name", tableName);
            if (Convert.ToInt64(existsCommand.ExecuteScalar()) == 0)
            {
                throw new ScriptSetInstallationException($"数据库中不存在表：{tableName}");
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteSqliteIdentifier(tableName)});";
            using SqliteDataReader reader = command.ExecuteReader();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                columns.Add(reader.GetString(reader.GetOrdinal("name")));
            }
            return columns;
        }

        public void Insert(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        )
        {
            Batteries_V2.Init();
            using var connection = new SqliteConnection(
                CreateSqliteConnectionString(target.Path)
            );
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                var nextIndexes = new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (DatabaseRowPlan plan in plans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string tableName = ScriptSetDatabaseRules.GetTableName(target.EngineType, plan.TableType);
                    AssignNextSqliteIndex(
                        connection,
                        transaction,
                        tableName,
                        plan,
                        nextIndexes
                    );
                    string columns = string.Join(
                        ", ",
                        plan.Values.Keys.Select(QuoteSqliteIdentifier)
                    );
                    string parameters = string.Join(
                        ", ",
                        plan.Values.Keys.Select((_, index) => $"$p{index}")
                    );
                    using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        $"INSERT INTO {QuoteSqliteIdentifier(tableName)} ({columns}) VALUES ({parameters});";
                    int parameterIndex = 0;
                    foreach (object? value in plan.Values.Values)
                    {
                        command.Parameters.AddWithValue(
                            $"$p{parameterIndex++}",
                            value ?? DBNull.Value
                        );
                    }
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public int Remove(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        )
        {
            Batteries_V2.Init();
            using var connection = new SqliteConnection(
                CreateSqliteConnectionString(target.Path)
            );
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                int removedCount = 0;
                foreach (DatabaseRowPlan plan in plans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string tableName = ScriptSetDatabaseRules.GetTableName(target.EngineType, plan.TableType);
                    KeyValuePair<string, object?>[] matchValues = ScriptSetDatabaseRules.GetDatabaseMatchValues(
                        plan
                    );

                    using SqliteCommand findCommand = connection.CreateCommand();
                    findCommand.Transaction = transaction;
                    string whereClause = AddSqliteMatchParameters(
                        findCommand,
                        matchValues
                    );
                    findCommand.CommandText =
                        $"SELECT {QuoteSqliteIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)}"
                        + $" FROM {QuoteSqliteIdentifier(tableName)}"
                        + $" WHERE {whereClause}"
                        + $" ORDER BY {QuoteSqliteIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)} DESC LIMIT 1;";
                    object? index = findCommand.ExecuteScalar();
                    if (index is null || index is DBNull)
                    {
                        continue;
                    }

                    using SqliteCommand deleteCommand = connection.CreateCommand();
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText =
                        $"DELETE FROM {QuoteSqliteIdentifier(tableName)}"
                        + $" WHERE {QuoteSqliteIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)} = $idx;";
                    deleteCommand.Parameters.AddWithValue("$idx", index);
                    removedCount += deleteCommand.ExecuteNonQuery();
                }

                transaction.Commit();
                return removedCount;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static string AddSqliteMatchParameters(
            SqliteCommand command,
            IReadOnlyList<KeyValuePair<string, object?>> matchValues
        )
        {
            var conditions = new List<string>(matchValues.Count);
            for (int index = 0; index < matchValues.Count; index++)
            {
                KeyValuePair<string, object?> matchValue = matchValues[index];
                string column = QuoteSqliteIdentifier(matchValue.Key);
                if (matchValue.Value is null)
                {
                    conditions.Add($"{column} IS NULL");
                    continue;
                }

                string parameterName = $"$match{index}";
                conditions.Add($"{column} = {parameterName}");
                command.Parameters.AddWithValue(parameterName, matchValue.Value);
            }
            return string.Join(" AND ", conditions);
        }

        private static void AssignNextSqliteIndex(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            DatabaseRowPlan plan,
            IDictionary<string, long> nextIndexes
        )
        {
            if (!nextIndexes.TryGetValue(tableName, out long nextIndex))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT MAX({QuoteSqliteIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)}) FROM {QuoteSqliteIdentifier(tableName)};";
                nextIndex = ScriptSetDatabaseRules.GetNextIndex(command.ExecuteScalar(), tableName);
            }

            plan.Values[ScriptSetDatabaseRules.DatabaseIndexColumn] = nextIndex;
            nextIndexes[tableName] = checked(nextIndex + 1);
        }

        private static string CreateSqliteConnectionString(string path) =>
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();

        private static string QuoteSqliteIdentifier(string identifier) =>
            $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
