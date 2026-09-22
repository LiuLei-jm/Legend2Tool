using Legend2Tool.WPF.Enums;
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
                HashSet<string> columns;
                try
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText =
                        $"SELECT * FROM {QuoteAccessIdentifier(group.Key)} WHERE 1 = 0";
                    using OleDbDataReader reader = command.ExecuteReader();
                    columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < reader.FieldCount; index++)
                    {
                        columns.Add(reader.GetName(index));
                    }
                }
                catch (OleDbException ex)
                {
                    throw new ScriptSetInstallationException(
                        $"无法读取 Access 数据表：{group.Key}",
                        ex
                    );
                }
                ScriptSetDatabaseRules.ValidateColumns(group, group.Key, columns);
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
                foreach (DatabaseRowPlan plan in plans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string tableName = ScriptSetDatabaseRules.GetTableName(target.EngineType, plan.TableType);
                    AssignNextAccessIndex(
                        connection,
                        transaction,
                        tableName,
                        plan,
                        nextIndexes
                    );
                    string columns = string.Join(
                        ", ",
                        plan.Values.Keys.Select(QuoteAccessIdentifier)
                    );
                    string parameters = string.Join(", ", plan.Values.Keys.Select(_ => "?"));
                    using OleDbCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        $"INSERT INTO {QuoteAccessIdentifier(tableName)} ({columns}) VALUES ({parameters})";
                    foreach (object? value in plan.Values.Values)
                    {
                        command.Parameters.Add(new OleDbParameter
                        {
                            Value = value ?? DBNull.Value
                        });
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
                foreach (DatabaseRowPlan plan in plans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string tableName = ScriptSetDatabaseRules.GetTableName(target.EngineType, plan.TableType);
                    KeyValuePair<string, object?>[] matchValues = ScriptSetDatabaseRules.GetDatabaseMatchValues(
                        plan
                    );

                    using OleDbCommand findCommand = connection.CreateCommand();
                    findCommand.Transaction = transaction;
                    string whereClause = AddAccessMatchParameters(
                        findCommand,
                        matchValues
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
                    deleteCommand.Parameters.Add(new OleDbParameter { Value = index });
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
            IReadOnlyList<KeyValuePair<string, object?>> matchValues
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
                command.Parameters.Add(new OleDbParameter { Value = matchValue.Value });
            }
            return string.Join(" AND ", conditions);
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
