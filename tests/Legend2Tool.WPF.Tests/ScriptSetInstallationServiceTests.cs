using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ScriptSets;
using Legend2Tool.WPF.Services.ScriptSets.Installation;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Database;
using Legend2Tool.WPF.Services.ServerConfiguration;
using Legend2Tool.WPF.State;
using Microsoft.Data.Sqlite;
using Serilog;
using SQLitePCL;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using GeeConfig = Legend2Tool.WPF.Models.M2Config.M2Config.GEEConfig;

namespace Legend2Tool.WPF.Tests;

public sealed class ScriptSetInstallationServiceTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    static ScriptSetInstallationServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void InjectSegments_ExistingTrigger_InsertsMarkedBlockAndIsIdempotent()
    {
        Guid scriptSetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid segmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var scriptFile = new ScriptFileInfo(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "QFunction-0.txt",
            "Mir200/Envir",
            ScriptFileType.Partial,
            null,
            [new ScriptSegmentInfo(segmentId, "@test", "SENDMSG 6 测试")]
        );
        Encoding encoding = new UTF8Encoding(false);
        byte[] original = encoding.GetBytes("原始内容\r\n[@TeSt]\r\n原触发内容");

        byte[] inserted = ScriptSegmentEditor.InjectSegments(
            original,
            encoding,
            0,
            scriptSetId,
            scriptFile
        );
        byte[] insertedAgain = ScriptSegmentEditor.InjectSegments(
            inserted,
            encoding,
            0,
            scriptSetId,
            scriptFile
        );

        string content = encoding.GetString(inserted);
        Assert.Contains(
            "[@TeSt]\r\n;---脚本插入--- ScriptSet=11111111111111111111111111111111;Segment=22222222222222222222222222222222\r\n",
            content,
            StringComparison.Ordinal
        );
        Assert.Contains("SENDMSG 6 测试\r\n;---插入结束---", content);
        Assert.EndsWith("原触发内容", content, StringComparison.Ordinal);
        byte[] originalPrefix = encoding.GetBytes("原始内容\r\n[@TeSt]\r\n");
        byte[] originalSuffix = encoding.GetBytes("原触发内容");
        Assert.True(inserted.AsSpan(0, originalPrefix.Length).SequenceEqual(originalPrefix));
        Assert.True(inserted.AsSpan(^originalSuffix.Length).SequenceEqual(originalSuffix));
        Assert.Equal(inserted, insertedAgain);
    }

    [Fact]
    public void InjectSegments_SpecialAndMissingTriggers_KeepTopAndBottomPositions()
    {
        Guid scriptSetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var scriptFile = new ScriptFileInfo(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "RobotManage.txt",
            "Mir200/Envir/Robot_def",
            ScriptFileType.Partial,
            null,
            [
                new ScriptSegmentInfo(null, "#top", "TOP-A"),
                new ScriptSegmentInfo(null, "#TOP", "TOP-B"),
                new ScriptSegmentInfo(null, "newTrigger", "NORMAL"),
                new ScriptSegmentInfo(null, "#bottom", "BOTTOM-A"),
                new ScriptSegmentInfo(null, "#BOTTOM", "BOTTOM-B")
            ]
        );
        Encoding encoding = new UTF8Encoding(false);

        byte[] output = ScriptSegmentEditor.InjectSegments(
            encoding.GetBytes("原文件内容"),
            encoding,
            0,
            scriptSetId,
            scriptFile
        );

        string content = encoding.GetString(output);
        Assert.StartsWith(";---脚本插入---", content, StringComparison.Ordinal);
        Assert.True(content.IndexOf("TOP-A", StringComparison.Ordinal)
            < content.IndexOf("TOP-B", StringComparison.Ordinal));
        Assert.Contains("[newTrigger]", content, StringComparison.Ordinal);
        Assert.True(content.IndexOf("BOTTOM-A", StringComparison.Ordinal)
            < content.IndexOf("BOTTOM-B", StringComparison.Ordinal));
        Assert.EndsWith(
            ";---插入结束--- ScriptSet=11111111111111111111111111111111;Segment=33333333333333333333333333333333-4",
            content,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void InjectSegments_TriggerAtEndOfFile_DoesNotAppendDuplicateTrigger()
    {
        Guid scriptSetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var scriptFile = new ScriptFileInfo(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "QFunction-0.txt",
            "Mir200/Envir",
            ScriptFileType.Partial,
            null,
            [
                new ScriptSegmentInfo(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "@lateTrigger",
                    "SENDMSG 6 末尾触发"
                )
            ]
        );
        Encoding encoding = new UTF8Encoding(false);

        byte[] output = ScriptSegmentEditor.InjectSegments(
            encoding.GetBytes("第一行\r\n第二行\r\n[@LateTrigger]"),
            encoding,
            0,
            scriptSetId,
            scriptFile
        );

        string content = encoding.GetString(output);
        Assert.Equal(
            1,
            CountOccurrences(content, "[@LateTrigger]", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            "[@LateTrigger]\r\n;---脚本插入---",
            content,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RemoveInsertedSegments_OnlyRemovesMarkedBlock()
    {
        Guid scriptSetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var scriptFile = new ScriptFileInfo(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "QFunction-0.txt",
            "Mir200/Envir",
            ScriptFileType.Partial,
            null,
            [
                new ScriptSegmentInfo(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "@test",
                    "SENDMSG 6 测试"
                )
            ]
        );
        Encoding encoding = new UTF8Encoding(false);
        const string original = "原始头部\r\n[@Test]\r\n原始尾部";
        byte[] inserted = ScriptSegmentEditor.InjectSegments(
            encoding.GetBytes(original),
            encoding,
            0,
            scriptSetId,
            scriptFile
        );

        byte[] removed = ScriptSegmentEditor.RemoveInsertedSegments(
            inserted,
            encoding,
            0,
            scriptSetId,
            scriptFile.FileName
        );

        string content = encoding.GetString(removed);
        Assert.Equal("原始头部\r\n[@Test]\r\n\r\n原始尾部", content);
        Assert.DoesNotContain("SENDMSG 6 测试", content, StringComparison.Ordinal);
        Assert.DoesNotContain(";---脚本插入---", content, StringComparison.Ordinal);
        Assert.DoesNotContain(";---插入结束---", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveInsertedSegments_IncompleteMarker_ThrowsWithoutChangingSource()
    {
        Guid scriptSetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Encoding encoding = new UTF8Encoding(false);
        byte[] source = encoding.GetBytes(
            "原始内容\r\n;---脚本插入--- ScriptSet=11111111111111111111111111111111;Segment=broken\r\n插入内容"
        );
        byte[] snapshot = source.ToArray();

        Assert.Throws<ScriptSetInstallationException>(() =>
            ScriptSegmentEditor.RemoveInsertedSegments(
                source,
                encoding,
                0,
                scriptSetId,
                "QFunction-0.txt"
            )
        );
        Assert.Equal(snapshot, source);
    }

    [Theory]
    [InlineData("@Main", "[@Main]")]
    [InlineData("[@Main]", "[@Main]")]
    [InlineData("#TOP", "#top")]
    [InlineData("#Bottom", "#bottom")]
    public void NormalizeTrigger_ValidValue_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, ScriptSegmentEditor.NormalizeTrigger(input));
    }

    [Fact]
    public void ResolveScriptPath_ParentTraversal_Throws()
    {
        string serverDirectory = Path.Combine(Path.GetTempPath(), "server");

        Assert.Throws<ScriptSetInstallationException>(() =>
            DeploymentPathResolver.ResolveScriptPath(
                serverDirectory,
                "../outside",
                "script.txt"
            )
        );
    }

    [Fact]
    public void ResolveMaterialPath_ParentTraversal_Throws()
    {
        string serverDirectory = Path.Combine(Path.GetTempPath(), "server");

        Assert.Throws<ScriptSetInstallationException>(() =>
            DeploymentPathResolver.ResolveMaterialPath(
                serverDirectory,
                "TestResource",
                "../../outside",
                "material.pak"
            )
        );
    }

    [Fact]
    public void ParseDatabaseValues_JsonObject_PreservesValueTypes()
    {
        Dictionary<string, object?> values =
            ScriptSetDatabaseDeployment.ParseDatabaseValues(
                """{"Idx":100,"Name":"测试物品","Enabled":true,"Memo":null}""",
                "测试数据"
            );

        Assert.Equal(100L, values["Idx"]);
        Assert.Equal("测试物品", values["Name"]);
        Assert.Equal(true, values["Enabled"]);
        Assert.Null(values["Memo"]);
    }

    [Fact]
    public void InsertSqliteRows_ExistingRows_AssignsSequentialIndexesAfterMaximum()
    {
        Batteries_V2.Init();
        string directory = CreateTempDirectory();
        string databasePath = Path.Combine(directory, "game.db");
        try
        {
            using (var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False"
            ))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE StdItems (Idx INTEGER NOT NULL, Name TEXT NOT NULL);"
                    + "INSERT INTO StdItems (Idx, Name) VALUES (41, '已有数据');";
                command.ExecuteNonQuery();
            }

            var target = new DatabaseTarget(
                databasePath,
                DatabaseProvider.Sqlite,
                EngineType.GEE
            );
            DatabaseRowPlan[] plans =
            [
                new(
                    GameDatabaseTableType.StdItems,
                    "测试物品一",
                    new Dictionary<string, object?>
                    {
                        ["Idx"] = 999L,
                        ["Name"] = "测试物品一"
                    }
                ),
                new(
                    GameDatabaseTableType.StdItems,
                    "测试物品二",
                    new Dictionary<string, object?> { ["Name"] = "测试物品二" }
                )
            ];

            new SqliteScriptSetDatabase().Insert(
                target,
                plans,
                CancellationToken.None
            );

            using (var verifyConnection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False"
            ))
            {
                verifyConnection.Open();
                using SqliteCommand verifyCommand = verifyConnection.CreateCommand();
                verifyCommand.CommandText =
                    "SELECT group_concat(Idx || ':' || Name, '|')"
                    + " FROM StdItems WHERE Idx > 41 ORDER BY Idx;";
                Assert.Equal("42:测试物品一|43:测试物品二", verifyCommand.ExecuteScalar());

                verifyCommand.CommandText = "SELECT COUNT(*) FROM StdItems WHERE Idx = 999;";
                Assert.Equal(0L, verifyCommand.ExecuteScalar());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_ScriptsAndDatabaseData_InstallsBothKinds()
    {
        string serverDirectory = CreateTempDirectory();
        try
        {
            string databasePath = CreateGameDatabase(serverDirectory);
            string partialPath = Path.Combine(
                serverDirectory,
                "Mir200",
                "Envir",
                "partial.txt"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            File.WriteAllText(
                partialPath,
                "原内容\r\n[@Login]\r\n原触发内容",
                Encoding.GetEncoding("GB18030")
            );
            ScriptSetInfo scriptSet = new(Guid.NewGuid(), "测试脚本套", null);
            ScriptSetDeploymentData deploymentData = new(
                [
                    new ScriptFileInfo(
                        Guid.NewGuid(),
                        "whole.txt",
                        "Mir200/Envir",
                        ScriptFileType.Whole,
                        "全量内容",
                        null
                    ),
                    new ScriptFileInfo(
                        Guid.NewGuid(),
                        "partial.txt",
                        "Mir200/Envir",
                        ScriptFileType.Partial,
                        null,
                        [new ScriptSegmentInfo(Guid.NewGuid(), "@login", "片段内容")]
                    )
                ],
                [
                    new ScriptSetDatabaseDataInfo(
                        Guid.NewGuid(),
                        scriptSet.Id,
                        GameDatabaseTableType.StdItems,
                        "测试物品",
                        """{"Name":"测试物品"}"""
                    )
                ]
            );
            ScriptSetInstallationService service = CreateInstallationService(
                serverDirectory,
                deploymentData
            );

            ScriptSetInstallationResult result = await service.InstallAsync(scriptSet);

            Encoding gb18030 = Encoding.GetEncoding("GB18030");
            Assert.Equal(
                "全量内容",
                File.ReadAllText(
                    Path.Combine(serverDirectory, "Mir200", "Envir", "whole.txt"),
                    gb18030
                )
            );
            string partialContent = File.ReadAllText(partialPath, gb18030);
            Assert.Contains("[@Login]\r\n;---脚本插入---", partialContent);
            Assert.Contains("片段内容", partialContent);
            Assert.EndsWith("原触发内容", partialContent, StringComparison.Ordinal);
            Assert.Equal("测试物品", ReadDatabaseName(databasePath, 42));
            Assert.Equal(2, result.ScriptFileCount);
            Assert.Equal(1, result.DatabaseRowCount);
        }
        finally
        {
            Directory.Delete(serverDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_MaterialFile_InstallsContentAndAppendsPakEntry()
    {
        string serverDirectory = CreateTempDirectory();
        try
        {
            Encoding gb18030 = Encoding.GetEncoding("GB18030");
            string launcherDirectory = Path.Combine(serverDirectory, "登录器");
            Directory.CreateDirectory(launcherDirectory);
            string pakPath = Path.Combine(launcherDirectory, "pak.txt");
            File.WriteAllText(pakPath, "existing.pak|old-password\r\n", gb18030);

            Guid materialId = Guid.NewGuid();
            byte[] materialContent = [0, 1, 2, 127, 128, 255];
            string sha256 = Convert.ToHexString(
                SHA256.HashData(materialContent)
            ).ToLowerInvariant();
            ScriptSetInfo scriptSet = new(Guid.NewGuid(), "素材脚本套", null);
            ScriptSetDeploymentData deploymentData = new(
                [],
                [],
                [
                    new MaterialFileInfo(
                        materialId,
                        "custom.pak",
                        "Data/Custom",
                        "new-password",
                        materialContent.Length,
                        sha256
                    )
                ]
            );
            ScriptSetInstallationService service = CreateInstallationService(
                serverDirectory,
                deploymentData,
                new Dictionary<Guid, byte[]> { [materialId] = materialContent }
            );

            ScriptSetInstallationResult result = await service.InstallAsync(scriptSet);

            string installedPath = Path.Combine(
                serverDirectory,
                "登录器",
                "补丁文件夹",
                "TestResource",
                "Data",
                "Custom",
                "custom.pak"
            );
            Assert.Equal(materialContent, File.ReadAllBytes(installedPath));
            string pakContent = File.ReadAllText(pakPath, gb18030);
            Assert.Equal(
                $"existing.pak|old-password\r\n{installedPath}|new-password\r\n",
                pakContent
            );
            Assert.Equal(0, result.ScriptFileCount);
            Assert.Equal(0, result.DatabaseRowCount);
            Assert.Equal(1, result.MaterialFileCount);
        }
        finally
        {
            Directory.Delete(serverDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveAsync_MaterialFile_RemovesContentAndPakEntry()
    {
        string serverDirectory = CreateTempDirectory();
        try
        {
            Encoding gb18030 = Encoding.GetEncoding("GB18030");
            Guid materialId = Guid.NewGuid();
            byte[] materialContent = [0, 1, 2, 127, 128, 255];
            string installedPath = Path.Combine(
                serverDirectory,
                "登录器",
                "补丁文件夹",
                "TestResource",
                "Data",
                "Custom",
                "custom.pak"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath)!);
            File.WriteAllBytes(installedPath, materialContent);

            string launcherDirectory = Path.Combine(serverDirectory, "登录器");
            Directory.CreateDirectory(launcherDirectory);
            string pakPath = Path.Combine(launcherDirectory, "pak.txt");
            File.WriteAllText(
                pakPath,
                $"keep.pak|keep-password\r\n{installedPath}|material-password\r\n",
                gb18030
            );
            ScriptSetInfo scriptSet = new(Guid.NewGuid(), "可删除素材脚本套", null);
            ScriptSetDeploymentData deploymentData = new(
                [],
                [],
                [
                    new MaterialFileInfo(
                        materialId,
                        "custom.pak",
                        "Data/Custom",
                        "material-password",
                        materialContent.Length,
                        Convert.ToHexString(SHA256.HashData(materialContent)).ToLowerInvariant()
                    )
                ]
            );
            ScriptSetInstallationService service = CreateInstallationService(
                serverDirectory,
                deploymentData
            );

            ScriptSetRemovalResult result = await service.RemoveAsync(scriptSet);

            Assert.False(File.Exists(installedPath));
            Assert.Equal(
                "keep.pak|keep-password\r\n",
                File.ReadAllText(pakPath, gb18030)
            );
            Assert.Equal(0, result.ScriptFileCount);
            Assert.Equal(0, result.DatabaseRowCount);
            Assert.Equal(1, result.MaterialFileCount);
        }
        finally
        {
            Directory.Delete(serverDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_DatabaseInsertFails_RestoresChangedScriptFiles()
    {
        string serverDirectory = CreateTempDirectory();
        try
        {
            string databasePath = CreateGameDatabase(serverDirectory);
            string scriptPath = Path.Combine(serverDirectory, "Mir200", "Envir", "whole.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(
                scriptPath,
                "原始脚本",
                Encoding.GetEncoding("GB18030")
            );
            string launcherDirectory = Path.Combine(serverDirectory, "登录器");
            Directory.CreateDirectory(launcherDirectory);
            string pakPath = Path.Combine(launcherDirectory, "pak.txt");
            File.WriteAllText(pakPath, "原始PAK配置", Encoding.GetEncoding("GB18030"));
            Guid materialId = Guid.NewGuid();
            byte[] materialContent = [10, 20, 30, 40];
            ScriptSetInfo scriptSet = new(Guid.NewGuid(), "回滚测试", null);
            ScriptSetDeploymentData deploymentData = new(
                [
                    new ScriptFileInfo(
                        Guid.NewGuid(),
                        "whole.txt",
                        "Mir200/Envir",
                        ScriptFileType.Whole,
                        "已替换脚本",
                        null
                    )
                ],
                [
                    new ScriptSetDatabaseDataInfo(
                        Guid.NewGuid(),
                        scriptSet.Id,
                        GameDatabaseTableType.StdItems,
                        "缺少名称",
                        """{"Idx":101}"""
                    )
                ],
                [
                    new MaterialFileInfo(
                        materialId,
                        "rollback.pak",
                        "Data",
                        "rollback-password",
                        materialContent.Length,
                        Convert.ToHexString(SHA256.HashData(materialContent)).ToLowerInvariant()
                    )
                ]
            );
            ScriptSetInstallationService service = CreateInstallationService(
                serverDirectory,
                deploymentData,
                new Dictionary<Guid, byte[]> { [materialId] = materialContent }
            );

            await Assert.ThrowsAsync<ScriptSetInstallationException>(() =>
                service.InstallAsync(scriptSet)
            );

            Assert.Equal(
                "原始脚本",
                File.ReadAllText(scriptPath, Encoding.GetEncoding("GB18030"))
            );
            Assert.False(File.Exists(Path.Combine(
                serverDirectory,
                "登录器",
                "补丁文件夹",
                "TestResource",
                "Data",
                "rollback.pak"
            )));
            Assert.Equal(
                "原始PAK配置",
                File.ReadAllText(pakPath, Encoding.GetEncoding("GB18030"))
            );
            Assert.Null(ReadDatabaseName(databasePath, 42));
            Assert.Equal("已有数据", ReadDatabaseName(databasePath, 41));
        }
        finally
        {
            Directory.Delete(serverDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveAsync_InstalledScriptsAndDatabaseData_RemovesOwnedDataOnly()
    {
        string serverDirectory = CreateTempDirectory();
        try
        {
            string databasePath = CreateGameDatabase(serverDirectory);
            string partialPath = Path.Combine(
                serverDirectory,
                "Mir200",
                "Envir",
                "partial.txt"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            File.WriteAllText(
                partialPath,
                "原始头部\r\n[@Login]\r\n原始尾部",
                Encoding.GetEncoding("GB18030")
            );
            ScriptSetInfo scriptSet = new(Guid.NewGuid(), "可删除脚本套", null);
            ScriptSetDeploymentData deploymentData = new(
                [
                    new ScriptFileInfo(
                        Guid.NewGuid(),
                        "whole.txt",
                        "Mir200/Envir",
                        ScriptFileType.Whole,
                        "全量内容",
                        null
                    ),
                    new ScriptFileInfo(
                        Guid.NewGuid(),
                        "partial.txt",
                        "Mir200/Envir",
                        ScriptFileType.Partial,
                        null,
                        [new ScriptSegmentInfo(Guid.NewGuid(), "@login", "片段内容")]
                    )
                ],
                [
                    new ScriptSetDatabaseDataInfo(
                        Guid.NewGuid(),
                        scriptSet.Id,
                        GameDatabaseTableType.StdItems,
                        "测试物品",
                        """{"Name":"测试物品"}"""
                    )
                ]
            );
            ScriptSetInstallationService service = CreateInstallationService(
                serverDirectory,
                deploymentData
            );
            await service.InstallAsync(scriptSet);

            ScriptSetRemovalResult result = await service.RemoveAsync(scriptSet);

            Assert.False(File.Exists(
                Path.Combine(serverDirectory, "Mir200", "Envir", "whole.txt")
            ));
            string partialContent = File.ReadAllText(
                partialPath,
                Encoding.GetEncoding("GB18030")
            );
            Assert.Contains("原始头部", partialContent, StringComparison.Ordinal);
            Assert.Contains("[@Login]", partialContent, StringComparison.Ordinal);
            Assert.Contains("原始尾部", partialContent, StringComparison.Ordinal);
            Assert.DoesNotContain("片段内容", partialContent, StringComparison.Ordinal);
            Assert.DoesNotContain(";---脚本插入---", partialContent, StringComparison.Ordinal);
            Assert.DoesNotContain(";---插入结束---", partialContent, StringComparison.Ordinal);
            Assert.Equal("已有数据", ReadDatabaseName(databasePath, 41));
            Assert.Null(ReadDatabaseName(databasePath, 42));
            Assert.Equal(2, result.ScriptFileCount);
            Assert.Equal(1, result.DatabaseRowCount);
        }
        finally
        {
            Directory.Delete(serverDirectory, recursive: true);
        }
    }

    private static ScriptSetInstallationService CreateInstallationService(
        string serverDirectory,
        ScriptSetDeploymentData deploymentData,
        IReadOnlyDictionary<Guid, byte[]>? materialContents = null
    )
    {
        var loadedConfig = new LoadedServerConfig(
            serverDirectory,
            EngineType.GEE,
            new GeeConfig { SqliteDBName = "Mud2/game.db" },
            new LauncherConfigBase { ResourcesDir = "TestResource" },
            new Setup(),
            []
        );
        var configService = new StubConfigService(loadedConfig);
        var configStore = new ConfigStore(configService, Logger);
        configStore.Receive(new ServerDirectoryChangedMessage(serverDirectory));
        return new ScriptSetInstallationService(
            new StubScriptSetService(deploymentData, materialContents),
            configStore,
            new EncodingService(),
            Logger
        );
    }

    private static string CreateGameDatabase(string serverDirectory)
    {
        Batteries_V2.Init();
        string mudDirectory = Path.Combine(serverDirectory, "Mud2");
        Directory.CreateDirectory(mudDirectory);
        string databasePath = Path.Combine(mudDirectory, "game.db");
        using var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False"
        );
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE StdItems (Idx INTEGER NOT NULL, Name TEXT NOT NULL);"
            + "INSERT INTO StdItems (Idx, Name) VALUES (41, '已有数据');";
        command.ExecuteNonQuery();
        return databasePath;
    }

    private static int CountOccurrences(
        string content,
        string value,
        StringComparison comparison
    )
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = content.IndexOf(value, startIndex, comparison)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }
        return count;
    }

    private static object? ReadDatabaseName(string databasePath, int index)
    {
        using var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False"
        );
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM StdItems WHERE Idx = $idx;";
        command.Parameters.AddWithValue("$idx", index);
        return command.ExecuteScalar();
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Legend2Tool.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StubScriptSetService : IScriptSetService
    {
        private readonly ScriptSetDeploymentData _deploymentData;
        private readonly IReadOnlyDictionary<Guid, byte[]> _materialContents;

        public StubScriptSetService(
            ScriptSetDeploymentData deploymentData,
            IReadOnlyDictionary<Guid, byte[]>? materialContents
        )
        {
            _deploymentData = deploymentData;
            _materialContents = materialContents ?? new Dictionary<Guid, byte[]>();
        }

        public Task<IReadOnlyList<ScriptSetInfo>> GetScriptSetsAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ScriptSetDeploymentData> GetDeploymentDataAsync(
            Guid scriptSetId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_deploymentData);

        public async Task DownloadMaterialFileAsync(
            Guid materialFileId,
            Stream destination,
            CancellationToken cancellationToken = default
        )
        {
            if (!_materialContents.TryGetValue(materialFileId, out byte[]? content))
            {
                throw new InvalidOperationException(
                    $"No test material content for {materialFileId}."
                );
            }
            await destination.WriteAsync(content, cancellationToken);
        }
    }

    private sealed class StubConfigService : IConfigService
    {
        private readonly LoadedServerConfig _loadedConfig;

        public StubConfigService(LoadedServerConfig loadedConfig)
        {
            _loadedConfig = loadedConfig;
        }

        public EngineType CheckEngineType(string serverDirectory) => _loadedConfig.EngineType;
        public Task<string> GetExternalIpAddressAsync() => throw new NotSupportedException();
        public bool CheckPorts(int[] portsToCheck) => throw new NotSupportedException();
        public string GetResourcesDirByGamePinyin(string launcherName) =>
            throw new NotSupportedException();
        public string GetLauncherName(ConfigStore configStore) => throw new NotSupportedException();
        public Task SaveConfigFileAsync(ConfigStore configStore) =>
            throw new NotSupportedException();
        public LoadedServerConfig LoadServerConfig(string serverDirectory) => _loadedConfig;
        public void ApplyDefaultAuxiliarySettings(ConfigStore configStore) =>
            throw new NotSupportedException();
        public Task GenerateCleanupScriptAsync(string baseDirectory) =>
            throw new NotSupportedException();
    }
}
