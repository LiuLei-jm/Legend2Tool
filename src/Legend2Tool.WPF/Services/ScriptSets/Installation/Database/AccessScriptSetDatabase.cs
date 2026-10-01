using Legend2Tool.WPF.Enums;
using System.Data;
using System.Data.OleDb;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database
{
    internal sealed class AccessScriptSetDatabase : IScriptSetDatabase
    {
        public void Validate(DatabaseTarget target, IReadOnlyList<DatabaseRowPlan> plans)
        {
            using var connection = new OleDbConnection(CreateAccessConnectionString(target.Path));
            connection.Open();
            ValidateAccessPlans(connection, target.EngineType, plans);
        }

        private static void ValidateAccessPlans(
            OleDbConnection connection,
            EngineType engineType,
            IReadOnlyList<DatabaseRowPlan> plans
        )
        {
            foreach (IGrouping<string, DatabaseRowPlan> group in plans.GroupBy(
                plan => ScriptSetDatabaseRules.GetTableName(engineType, plan.TableType),
                StringComparer.OrdinalIgnoreCase
            ))
            {
                IReadOnlyDictionary<string, OleDbType> columns = GetAccessColumns(
                    connection,
                    null,
                    group.Key
                );
                ScriptSetDatabaseRules.ValidateColumns(
                    group,
                    group.Key,
                    columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                );
            }
        }

        public void Insert(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        )
        {
            using var connection = new OleDbConnection(CreateAccessConnectionString(target.Path));
            connection.Open();
            using OleDbTransaction transaction = connection.BeginTransaction();
            try
            {
                var nextIndexes = new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase
                );
                var tableColumns = new Dictionary<string, IReadOnlyDictionary<string, OleDbType>>(
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (DatabaseRowPlan plan in plans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string tableName = ScriptSetDatabaseRules.GetTableName(target.EngineType, plan.TableType);
                    if (!tableColumns.TryGetValue(tableName, out IReadOnlyDictionary<string, OleDbType>? columnsByName))
                    {
                        columnsByName = GetAccessColumns(connection, transaction, tableName);
                        tableColumns.Add(tableName, columnsByName);
                    }
                    AssignNextAccessIndex(
                        connection,
                        transaction,
                        tableName,
                        plan,
                        nextIndexes
                    );
                    KeyValuePair<string, object?>[] values = plan.Values.ToArray();
                    string columns = string.Join(
                        ", ",
                        values.Select(value => QuoteAccessIdentifier(value.Key))
                    );
                    string parameters = string.Join(", ", values.Select(_ => "?"));
                    using OleDbCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        $"INSERT INTO {QuoteAccessIdentifier(tableName)} ({columns}) VALUES ({parameters})";
                    foreach (KeyValuePair<string, object?> value in values)
                    {
                        command.Parameters.Add(CreateAccessParameter(
                            tableName,
                            value.Key,
                            value.Value,
                            columnsByName
                        ));
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
            using var connection = new OleDbConnection(CreateAccessConnectionString(target.Path));
            connection.Open();
            using OleDbTransaction transaction = connection.BeginTransaction();
            try
            {
                int removedCount = 0;
                var tableColumns = new Dictionary<string, IReadOnlyDictionary<string, OleDbType>>(
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (DatabaseRowPlan plan in plans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string tableName = ScriptSetDatabaseRules.GetTableName(target.EngineType, plan.TableType);
                    if (!tableColumns.TryGetValue(tableName, out IReadOnlyDictionary<string, OleDbType>? columnsByName))
                    {
                        columnsByName = GetAccessColumns(connection, transaction, tableName);
                        tableColumns.Add(tableName, columnsByName);
                    }
                    KeyValuePair<string, object?>[] matchValues = ScriptSetDatabaseRules.GetDatabaseMatchValues(
                        plan
                    );

                    using OleDbCommand findCommand = connection.CreateCommand();
                    findCommand.Transaction = transaction;
                    string whereClause = AddAccessMatchParameters(
                        findCommand,
                        matchValues,
                        tableName,
                        columnsByName
                    );
                    findCommand.CommandText =
                        $"SELECT TOP 1 {QuoteAccessIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)}"
                        + $" FROM {QuoteAccessIdentifier(tableName)}"
                        + $" WHERE {whereClause}"
                        + $" ORDER BY {QuoteAccessIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)} DESC";
                    object? index = findCommand.ExecuteScalar();
                    if (index is null || index is DBNull)
                    {
                        continue;
                    }

                    using OleDbCommand deleteCommand = connection.CreateCommand();
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText =
                        $"DELETE FROM {QuoteAccessIdentifier(tableName)}"
                        + $" WHERE {QuoteAccessIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)} = ?";
                    deleteCommand.Parameters.Add(CreateAccessParameter(
                        tableName,
                        ScriptSetDatabaseRules.DatabaseIndexColumn,
                        index,
                        columnsByName
                    ));
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

        private static string AddAccessMatchParameters(
            OleDbCommand command,
            IReadOnlyList<KeyValuePair<string, object?>> matchValues,
            string tableName,
            IReadOnlyDictionary<string, OleDbType> columnsByName
        )
        {
            var conditions = new List<string>(matchValues.Count);
            foreach (KeyValuePair<string, object?> matchValue in matchValues)
            {
                string column = QuoteAccessIdentifier(matchValue.Key);
                if (matchValue.Value is null)
                {
                    conditions.Add($"{column} IS NULL");
                    continue;
                }

                conditions.Add($"{column} = ?");
                command.Parameters.Add(CreateAccessParameter(
                    tableName,
                    matchValue.Key,
                    matchValue.Value,
                    columnsByName
                ));
            }
            return string.Join(" AND ", conditions);
        }

        private static IReadOnlyDictionary<string, OleDbType> GetAccessColumns(
            OleDbConnection connection,
            OleDbTransaction? transaction,
            string tableName
        )
        {
            try
            {
                using OleDbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT * FROM {QuoteAccessIdentifier(tableName)} WHERE 1 = 0";
                using OleDbDataReader reader = command.ExecuteReader();
                using DataTable? schema = reader.GetSchemaTable();
                if (schema is null
                    || !schema.Columns.Contains("ColumnName")
                    || !schema.Columns.Contains("ProviderType"))
                {
                    throw new ScriptSetInstallationException(
                        $"无法获取 Access 数据表 {tableName} 的字段类型。"
                    );
                }

                var columns = new Dictionary<string, OleDbType>(
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (DataRow row in schema.Rows)
                {
                    string name = (string)row["ColumnName"];
                    columns.Add(name, (OleDbType)Convert.ToInt32(row["ProviderType"]));
                }
                return columns;
            }
            catch (OleDbException ex)
            {
                throw new ScriptSetInstallationException(
                    $"无法读取 Access 数据表：{tableName}",
                    ex
                );
            }
        }

        private static OleDbParameter CreateAccessParameter(
            string tableName,
            string columnName,
            object? value,
            IReadOnlyDictionary<string, OleDbType> columnsByName
        )
        {
            if (!columnsByName.TryGetValue(columnName, out OleDbType columnType))
            {
                throw new ScriptSetInstallationException(
                    $"数据库表 {tableName} 不存在字段 {columnName}。"
                );
            }

            object parameterValue = value ?? DBNull.Value;
            if (value is long integer)
            {
                try
                {
                    parameterValue = columnType switch
                    {
                        OleDbType.Integer => checked((int)integer),
                        OleDbType.SmallInt => checked((short)integer),
                        OleDbType.TinyInt => checked((sbyte)integer),
                        OleDbType.UnsignedTinyInt => checked((byte)integer),
                        OleDbType.UnsignedSmallInt => checked((ushort)integer),
                        OleDbType.UnsignedInt => checked((uint)integer),
                        OleDbType.UnsignedBigInt => checked((ulong)integer),
                        _ => integer
                    };
                }
                catch (OverflowException ex)
                {
                    throw new ScriptSetInstallationException(
                        $"数据库字段 {tableName}.{columnName} 的值 {integer} 超出字段范围。",
                        ex
                    );
                }
            }

            return new OleDbParameter
            {
                OleDbType = columnType,
                Value = parameterValue
            };
        }

        private static void AssignNextAccessIndex(
            OleDbConnection connection,
            OleDbTransaction transaction,
            string tableName,
            DatabaseRowPlan plan,
            IDictionary<string, long> nextIndexes
        )
        {
            if (!nextIndexes.TryGetValue(tableName, out long nextIndex))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT MAX({QuoteAccessIdentifier(ScriptSetDatabaseRules.DatabaseIndexColumn)}) FROM {QuoteAccessIdentifier(tableName)}";
                nextIndex = ScriptSetDatabaseRules.GetNextIndex(command.ExecuteScalar(), tableName);
            }

            plan.Values[ScriptSetDatabaseRules.DatabaseIndexColumn] = nextIndex;
            nextIndexes[tableName] = checked(nextIndex + 1);
        }

        private static string CreateAccessConnectionString(string path) =>
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={path};Persist Security Info=False;";

        private static string QuoteAccessIdentifier(string identifier) =>
            $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
