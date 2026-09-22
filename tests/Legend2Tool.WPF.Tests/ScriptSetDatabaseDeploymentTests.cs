using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.ScriptSets;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Database;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using System.Data.OleDb;
using System.Diagnostics;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class ScriptSetDatabaseDeploymentTests
{
    [Theory]
    [InlineData(EngineType.GEE, false)]
    [InlineData(EngineType.GXX, false)]
    [InlineData(EngineType.LF, false)]
    [InlineData(EngineType.V8, false)]
    [InlineData(EngineType.BLUE, false)]
    [InlineData(EngineType.HGE, false)]
    [InlineData(EngineType.GOM, true)]
    [InlineData(EngineType.NEWGOM, true)]
    public void ResolveDatabaseTarget_SupportedEngine_UsesConfiguredPathAndProvider(EngineType engine, bool access)
    {
        using var directory = new TestDirectory();
        string path = Path.Combine(directory.Path, "Mud2", "configured.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        const string relativePath = "Mud2/configured.db";
        M2ConfigBase config = engine switch
        {
            EngineType.GOM or EngineType.NEWGOM => new GOMConfig { AccessFileName = relativePath },
            EngineType.BLUE => new BLUEConfig { DataTableFile = relativePath },
            EngineType.HGE => new HGEConfig { SQLiteName = relativePath },
            _ => new GEEConfig { SqliteDBName = relativePath }
        };

        DatabaseTarget target = ScriptSetDatabaseDeployment.ResolveDatabaseTarget(directory.Path, engine, config);

        Assert.Equal(path, target.Path);
        Assert.Equal(access ? DatabaseProvider.Access : DatabaseProvider.Sqlite, target.Provider);
        Assert.Equal(engine, target.EngineType);
    }

    [Fact]
    public void ResolveDatabaseTarget_UnsupportedEngine_ThrowsBeforeOpeningDatabase()
    {
        using var directory = new TestDirectory();
        Assert.Throws<ScriptSetInstallationException>(() =>
            ScriptSetDatabaseDeployment.ResolveDatabaseTarget(directory.Path, EngineType.Unknown, new M2ConfigBase()));
    }

    [Theory]
    [InlineData(EngineType.GEE, "StdItems", "Monster", "Magic")]
    [InlineData(EngineType.GXX, "StdItems", "Monster", "Magic")]
    [InlineData(EngineType.LF, "StdItems", "Monster", "Magic")]
    [InlineData(EngineType.V8, "StdItems", "Monster", "Magic")]
    [InlineData(EngineType.BLUE, "item", "monster", "magic")]
    [InlineData(EngineType.HGE, "StdItems", "Monster", "Magic")]
    public void DatabaseDeployment_SqliteEngines_InstallsAndRemovesAllMappedTables(
        EngineType engine, string items, string monsters, string magic)
    {
        Batteries_V2.Init();
        using var directory = new TestDirectory();
        var target = new DatabaseTarget(Path.Combine(directory.Path, "game.db"), DatabaseProvider.Sqlite, engine);
        using var connection = new SqliteConnection($"Data Source={target.Path};Pooling=False");
        connection.Open();
        foreach (string table in new[] { items, monsters, magic })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE [{table}] (Idx INTEGER NOT NULL, Name TEXT NOT NULL, Note TEXT);"
                + $"INSERT INTO [{table}] VALUES (41, 'original', NULL);";
            command.ExecuteNonQuery();
        }
        var plans = new[] { GameDatabaseTableType.StdItems, GameDatabaseTableType.Monster, GameDatabaseTableType.Magic }
            .Select(table => new DatabaseRowPlan(table, "new", new()
            {
                ["Name"] = "中文'quoted", ["Note"] = null, ["Idx"] = 999L
            })).ToArray();

        ScriptSetDatabaseDeployment.ValidateDatabasePlans(target, plans);
        ScriptSetDatabaseDeployment.InsertDatabaseRows(target, plans, CancellationToken.None);

        foreach (string table in new[] { items, monsters, magic })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Name FROM [{table}] WHERE Idx = 42;";
            Assert.Equal("中文'quoted", command.ExecuteScalar());
        }
        ScriptSetDatabaseDeployment.ValidateDatabaseRemovalPlans(plans);
        Assert.Equal(3, ScriptSetDatabaseDeployment.RemoveDatabaseRows(target, plans, CancellationToken.None));
        foreach (string table in new[] { items, monsters, magic })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE Idx = 41 AND Name = 'original';";
            Assert.Equal(1L, command.ExecuteScalar());
            command.CommandText = $"SELECT COUNT(*) FROM [{table}];";
            Assert.Equal(1L, command.ExecuteScalar());
        }
    }

    [AccessTheory]
    [InlineData(EngineType.GOM)]
    [InlineData(EngineType.NEWGOM)]
    public void DatabaseDeployment_AccessEngines_InstallsAndRemovesAllMappedTables(EngineType engine)
    {
        RunAccess(() =>
        {
            using var directory = new TestDirectory();
            var target = CreateAccessDatabase(directory, engine);
            using var connection = OpenAccess(target.Path);
            foreach (string table in new[] { "StdItems", "Monster", "Magic" })
            {
                ExecuteAccess(connection, $"CREATE TABLE [{table}] ([Idx] INTEGER NOT NULL, [Name] TEXT(255) NOT NULL, [Note] TEXT(255))");
                ExecuteAccess(connection, $"INSERT INTO [{table}] ([Idx], [Name]) VALUES (41, 'original')");
            }
            connection.Close();
            var plans = new[] { GameDatabaseTableType.StdItems, GameDatabaseTableType.Monster, GameDatabaseTableType.Magic }
                .Select(table => new DatabaseRowPlan(table, "new", new()
                {
                    ["Name"] = "中文'quoted", ["Note"] = null, ["Idx"] = 999L
                })).ToArray();

            ScriptSetDatabaseDeployment.ValidateDatabasePlans(target, plans);
            ScriptSetDatabaseDeployment.InsertDatabaseRows(target, plans, CancellationToken.None);

            connection.Open();
            foreach (string table in new[] { "StdItems", "Monster", "Magic" })
            {
                Assert.Equal("中文'quoted", ScalarAccess(connection, $"SELECT [Name] FROM [{table}] WHERE [Idx] = 42"));
            }
            connection.Close();
            ScriptSetDatabaseDeployment.ValidateDatabaseRemovalPlans(plans);
            Assert.Equal(3, ScriptSetDatabaseDeployment.RemoveDatabaseRows(target, plans, CancellationToken.None));
            connection.Open();
            foreach (string table in new[] { "StdItems", "Monster", "Magic" })
            {
                Assert.Equal(1, Convert.ToInt32(ScalarAccess(connection, $"SELECT COUNT(*) FROM [{table}]")));
                Assert.Equal("original", ScalarAccess(connection, $"SELECT [Name] FROM [{table}] WHERE [Idx] = 41"));
            }
        });
    }

    [AccessTheory]
    [InlineData(EngineType.GOM)]
    [InlineData(EngineType.NEWGOM)]
    public void Insert_AccessSecondRowFails_RollsBackFirstRow(EngineType engine)
    {
        RunAccess(() =>
        {
            using var directory = new TestDirectory();
            var target = CreateAccessDatabase(directory, engine);
            using var connection = OpenAccess(target.Path);
            ExecuteAccess(connection, "CREATE TABLE StdItems ([Idx] INTEGER NOT NULL, [Name] TEXT(255) NOT NULL, [Note] TEXT(255))");
            ExecuteAccess(connection, "CREATE UNIQUE INDEX UniqueName ON StdItems ([Name])");
            ExecuteAccess(connection, "INSERT INTO StdItems ([Idx], [Name]) VALUES (41, 'original')");
            connection.Close();
            DatabaseRowPlan[] plans = [
                new(GameDatabaseTableType.StdItems, "valid", new() { ["Name"] = "first", ["Note"] = null, ["Idx"] = 999L }),
                new(GameDatabaseTableType.StdItems, "duplicate", new() { ["Name"] = "original", ["Note"] = null, ["Idx"] = 999L })
            ];
            ScriptSetDatabaseDeployment.ValidateDatabasePlans(target, plans);

            Assert.Throws<OleDbException>(() =>
                ScriptSetDatabaseDeployment.InsertDatabaseRows(target, plans, CancellationToken.None));

            connection.Open();
            Assert.Equal(1, Convert.ToInt32(ScalarAccess(connection, "SELECT COUNT(*) FROM StdItems")));
            Assert.Equal("original", ScalarAccess(connection, "SELECT [Name] FROM StdItems WHERE [Idx] = 41"));
        });
    }

    [AccessTheory]
    [InlineData(EngineType.GOM)]
    [InlineData(EngineType.NEWGOM)]
    public void Insert_AccessCancelledBeforeSecondRow_RollsBackFirstRow(EngineType engine)
    {
        RunAccess(() =>
        {
            using var directory = new TestDirectory();
            var target = CreateAccessDatabase(directory, engine);
            using var connection = OpenAccess(target.Path);
            ExecuteAccess(connection, "CREATE TABLE StdItems ([Idx] INTEGER NOT NULL, [Name] TEXT(255) NOT NULL, [Note] TEXT(255))");
            ExecuteAccess(connection, "INSERT INTO StdItems ([Idx], [Name]) VALUES (41, 'original')");
            connection.Close();
            using var cancellation = new CancellationTokenSource();
            DatabaseRowPlan[] plans = [
                new(GameDatabaseTableType.StdItems, "first", new() { ["Name"] = "first", ["Note"] = null, ["Idx"] = 999L }),
                new(GameDatabaseTableType.StdItems, "second", new() { ["Name"] = "second", ["Note"] = null, ["Idx"] = 999L })
            ];
            ScriptSetDatabaseDeployment.ValidateDatabasePlans(target, plans);

            Assert.ThrowsAny<OperationCanceledException>(() =>
                ScriptSetDatabaseDeployment.InsertDatabaseRows(target,
                    new CancelAfterFirstRow(plans, cancellation), cancellation.Token));

            // Idx is assigned inside the transaction, proving that the first row was reached.
            Assert.Equal(42L, plans[0].Values["Idx"]);
            connection.Open();
            Assert.Equal(1, Convert.ToInt32(ScalarAccess(connection, "SELECT COUNT(*) FROM StdItems")));
            Assert.Equal("original", ScalarAccess(connection, "SELECT [Name] FROM StdItems WHERE [Idx] = 41"));
        });
    }

    private sealed class CancelAfterFirstRow(DatabaseRowPlan[] rows, CancellationTokenSource cancellation)
        : IReadOnlyList<DatabaseRowPlan>
    {
        public int Count => rows.Length;
        public DatabaseRowPlan this[int index] => rows[index];
        public IEnumerator<DatabaseRowPlan> GetEnumerator()
        {
            yield return rows[0];
            cancellation.Cancel();
            yield return rows[1];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void RunAccess(Action operation)
    {
        AccessTestThread.Run(operation);
    }

    private static class AccessTestThread
    {
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = new();

        static AccessTestThread()
        {
            // Keep one COM apartment alive across cases; ACE retains pooled native objects.
            var thread = new Thread(() =>
            {
                foreach (Action action in Queue.GetConsumingEnumerable())
                    action();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        internal static void Run(Action operation)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(() =>
            {
                try
                {
                    operation();
                    completed.SetResult();
                }
                catch (Exception exception)
                {
                    completed.SetException(exception);
                }
            });
            completed.Task.GetAwaiter().GetResult();
        }
    }

    private static DatabaseTarget CreateAccessDatabase(TestDirectory directory, EngineType engine)
    {
        string path = Path.Combine(directory.Path, "game.accdb");
        // Create the fixture outside the test host so ADOX's native COM lifetime is isolated.
        string scriptPath = Path.Combine(directory.Path, "create.vbs");
        File.WriteAllText(scriptPath, "Set catalog = CreateObject(\"ADOX.Catalog\")\r\n"
            + "catalog.Create \"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=\" & WScript.Arguments(0)\r\n"
            + "catalog.ActiveConnection.Close\r\nSet catalog = Nothing\r\n");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cscript.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "//Nologo", "//B", scriptPath, path })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Access test database creation timed out.");
        }
        Assert.True(process.ExitCode == 0, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        return new DatabaseTarget(path, DatabaseProvider.Access, engine);
    }

    private static OleDbConnection OpenAccess(string path)
    {
        var connection = new OleDbConnection($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={path};Persist Security Info=False;");
        connection.Open();
        return connection;
    }

    private static void ExecuteAccess(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? ScalarAccess(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public sealed class AccessTheoryAttribute : TheoryAttribute
    {
        public AccessTheoryAttribute()
        {
            if (Type.GetTypeFromProgID("Microsoft.ACE.OLEDB.12.0") is null
                || Type.GetTypeFromProgID("ADOX.Catalog") is null
                || Type.GetTypeFromProgID("VBScript") is null)
                Skip = "Requires ACE OLEDB 12.0, ADOX and VBScript for the test process architecture.";
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Legend2Tool.Tests", Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            // ACE can release native file handles shortly after the connection closes.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
