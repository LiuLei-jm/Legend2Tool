using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ScriptSets;
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

public sealed class ScriptSetInstallationRegressionTests
{
    static ScriptSetInstallationRegressionTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Batteries_V2.Init();
    }

    [Theory]
    [InlineData("utf-8", true, "\r\n")]
    [InlineData("utf-8", false, "\n")]
    [InlineData("utf-16", true, "\r")]
    [InlineData("utf-16BE", true, "\r\n")]
    [InlineData("GB18030", false, "\r\n")]
    public async Task InstallAsync_PartialScript_PreservesBomEncodingAndUnchangedBytes(
        string encodingName, bool bom, string newLine)
    {
        using var fixture = new DeploymentFixture();
        Encoding encoding = encodingName == "utf-8"
            ? new UTF8Encoding(bom, true)
            : Encoding.GetEncoding(encodingName);
        byte[] preamble = bom ? encoding.GetPreamble() : [];
        string prefix = "原始头部" + newLine + "[@Login]" + newLine;
        byte[] original = [.. preamble, .. encoding.GetBytes(prefix + "原始尾部")];
        File.WriteAllBytes(fixture.ScriptPath, original);
        fixture.Data = new([
            new(Guid.NewGuid(), "whole.txt", "Mir200/Envir", ScriptFileType.Partial,
                null, [new(Guid.NewGuid(), "@login", "新增中文内容")])
        ], []);
        var service = fixture.CreateService();

        await service.InstallAsync(fixture.ScriptSet);
        byte[] installed = File.ReadAllBytes(fixture.ScriptPath);

        Assert.True(installed.AsSpan().StartsWith([.. preamble, .. encoding.GetBytes(prefix)]));
        Assert.True(installed.AsSpan().EndsWith(encoding.GetBytes("原始尾部")));
        string installedText = encoding.GetString(installed.AsSpan(preamble.Length));
        Assert.Contains("新增中文内容" + newLine, installedText);
        await service.RemoveAsync(fixture.ScriptSet);
        byte[] removed = File.ReadAllBytes(fixture.ScriptPath);
        Assert.True(removed.AsSpan().StartsWith([.. preamble, .. encoding.GetBytes(prefix)]));
        Assert.True(removed.AsSpan().EndsWith(encoding.GetBytes("原始尾部")));
        Assert.DoesNotContain("新增中文内容", encoding.GetString(removed));
    }

    [Theory]
    [InlineData("download")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("cancel")]
    public async Task InstallAsync_MaterialPreparationFails_LeavesTargetFilesUnchanged(string failure)
    {
        using var fixture = new DeploymentFixture();
        using var cancellation = new CancellationTokenSource();
        MaterialFileInfo material = fixture.Material;
        if (failure == "size") material = material with { FileSize = material.FileSize + 1 };
        if (failure == "hash") material = material with { Sha256 = new string('0', 64) };
        fixture.Data = new([fixture.WholeScript], [], [material]);
        fixture.Download = failure switch
        {
            "download" => (_, _) => throw new IOException("Simulated download failure."),
            "cancel" => (_, _) =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            },
            _ => null
        };
        var original = fixture.ReadFiles();

        if (failure == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.CreateService().InstallAsync(fixture.ScriptSet, cancellation.Token));
        else if (failure == "download")
            await Assert.ThrowsAsync<IOException>(() => fixture.CreateService().InstallAsync(fixture.ScriptSet));
        else
            await Assert.ThrowsAsync<ScriptSetInstallationException>(() => fixture.CreateService().InstallAsync(fixture.ScriptSet));

        fixture.AssertFilesEqual(original);
        Assert.NotNull(fixture.DownloadPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.DownloadPath)));
    }

    [Fact]
    public async Task InstallAsync_SecondDatabaseRowFails_RestoresFilesAndRollsBackFirstRow()
    {
        using var fixture = new DeploymentFixture();
        fixture.Data = new(
            [fixture.WholeScript, fixture.WholeScript with { Id = Guid.NewGuid(), FileName = "new.txt" }],
            [fixture.Row("first"), fixture.Row("invalid") with { DataJson = "{\"Idx\":999}" }],
            [fixture.Material]);
        var original = fixture.ReadFiles();

        await Assert.ThrowsAsync<ScriptSetInstallationException>(() =>
            fixture.CreateService().InstallAsync(fixture.ScriptSet));

        fixture.AssertFilesEqual(original);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, "new.txt")));
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM StdItems"));
        Assert.Equal("existing", fixture.Scalar("SELECT Name FROM StdItems WHERE Idx = 41"));
    }

    [Fact]
    public async Task RemoveAsync_SecondDatabaseDeleteFails_RestoresAllFilesAndDeletedRow()
    {
        using var fixture = new DeploymentFixture();
        fixture.Data = new([fixture.WholeScript], [fixture.Row("first"), fixture.Row("second")], [fixture.Material]);
        var service = fixture.CreateService();
        await service.InstallAsync(fixture.ScriptSet);
        var installed = fixture.ReadFiles();
        fixture.Execute("CREATE TRIGGER stop_delete BEFORE DELETE ON StdItems "
            + "WHEN OLD.Name = 'second' BEGIN SELECT RAISE(ABORT, 'simulated delete failure'); END;");

        await Assert.ThrowsAsync<ScriptSetInstallationException>(() => service.RemoveAsync(fixture.ScriptSet));

        fixture.AssertFilesEqual(installed);
        Assert.Equal(3L, fixture.Scalar("SELECT COUNT(*) FROM StdItems"));
    }

    [Fact]
    public async Task InstallAsync_CancelledAfterFirstScriptWrite_RestoresOriginalFiles()
    {
        using var fixture = new DeploymentFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Data = new(
            [fixture.WholeScript, fixture.WholeScript with { Id = Guid.NewGuid(), FileName = "new.txt" }], []);
        var original = fixture.ReadFiles();
        var encoding = new CallbackEncodingService(() => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.CreateService(encoding).InstallAsync(fixture.ScriptSet, cancellation.Token));

        Assert.True(encoding.WasCalled);
        fixture.AssertFilesEqual(original);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, "new.txt")));
    }

    [Theory]
    [InlineData("script")]
    [InlineData("material")]
    public async Task RemoveAsync_InstalledFileModified_LeavesAllFilesAndDatabaseUnchanged(string modifiedFile)
    {
        using var fixture = new DeploymentFixture();
        fixture.Data = new([fixture.WholeScript], [fixture.Row("first")], [fixture.Material]);
        var service = fixture.CreateService();
        await service.InstallAsync(fixture.ScriptSet);
        if (modifiedFile == "script")
            File.AppendAllText(fixture.ScriptPath, "user edit", Encoding.UTF8);
        else
            File.WriteAllBytes(fixture.MaterialPath, [4, 3, 2, 1]);
        var modified = fixture.ReadFiles();

        await Assert.ThrowsAsync<ScriptSetInstallationException>(() => service.RemoveAsync(fixture.ScriptSet));

        fixture.AssertFilesEqual(modified);
        Assert.Equal(2L, fixture.Scalar("SELECT COUNT(*) FROM StdItems"));
    }

    private sealed class CallbackEncodingService(Action onDetect) : IEncodingService
    {
        private readonly EncodingService _inner = new();
        public bool WasCalled { get; private set; }
        public EncodingDetectionResult DetectFileEncodingResult(string path)
        {
            WasCalled = true;
            onDetect();
            return _inner.DetectFileEncodingResult(path);
        }
        public Encoding DetectFileEncoding(string path) => _inner.DetectFileEncoding(path);
        public Encoding DetectFileEncoding(string path, Encoding fallback) => _inner.DetectFileEncoding(path, fallback);
        public Encoding DetectBom(byte[] buffer) => _inner.DetectBom(buffer);
        public Encoding GetEncodingByName(string name) => _inner.GetEncodingByName(name);
        public void ConvertFileEncoding(string input, string output, Encoding encoding, string target) =>
            _inner.ConvertFileEncoding(input, output, encoding, target);
        public void ConvertFileEncoding(string input, string output, Encoding encoding, string target, string backup) =>
            _inner.ConvertFileEncoding(input, output, encoding, target, backup);
    }

    private sealed class DeploymentFixture : IScriptSetService, IConfigService, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Legend2Tool.Tests", Guid.NewGuid().ToString("N"));
        private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
        private readonly byte[] _materialBytes = [1, 2, 3, 4];
        public string ScriptPath => Path.Combine(_root, "Mir200", "Envir", "whole.txt");
        public string MaterialPath => Path.Combine(_root, "登录器", "补丁文件夹", "Resource", "Data", "test.pak");
        private string PakPath => Path.Combine(_root, "登录器", "pak.txt");
        private string DatabasePath => Path.Combine(_root, "Mud2", "game.db");
        public ScriptSetInfo ScriptSet { get; } = new(Guid.NewGuid(), "Regression", null);
        public ScriptFileInfo WholeScript { get; } = new(Guid.NewGuid(), "whole.txt", "Mir200/Envir", ScriptFileType.Whole, "替换内容", null);
        public MaterialFileInfo Material { get; }
        public ScriptSetDeploymentData Data { get; set; } = new([], [], []);
        public Func<Stream, CancellationToken, Task>? Download { get; set; }
        public string? DownloadPath { get; private set; }

        public DeploymentFixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            File.WriteAllText(ScriptPath, "原始脚本\r\n", new UTF8Encoding(true));
            File.WriteAllText(PakPath, "existing.pak|password\r\n", new UTF8Encoding(true));
            File.WriteAllBytes(MaterialPath, [9, 8, 7]);
            Material = new(Guid.NewGuid(), "test.pak", "Data", "password", _materialBytes.Length,
                Convert.ToHexString(SHA256.HashData(_materialBytes)));
            Execute("CREATE TABLE StdItems (Idx INTEGER NOT NULL, Name TEXT NOT NULL);"
                + "INSERT INTO StdItems VALUES (41, 'existing');");
        }

        public ScriptSetDatabaseDataInfo Row(string name) => new(Guid.NewGuid(), ScriptSet.Id,
            GameDatabaseTableType.StdItems, name, System.Text.Json.JsonSerializer.Serialize(new { Name = name }));

        public ScriptSetInstallationService CreateService(IEncodingService? encoding = null)
        {
            var store = new ConfigStore(this, _logger);
            store.Receive(new ServerDirectoryChangedMessage(_root));
            return new ScriptSetInstallationService(this, store, encoding ?? new EncodingService(), _logger);
        }

        public Dictionary<string, byte[]> ReadFiles() => new[] { ScriptPath, MaterialPath, PakPath }
            .ToDictionary(path => path, File.ReadAllBytes);

        public void AssertFilesEqual(Dictionary<string, byte[]> expected)
        {
            foreach (var (path, bytes) in expected)
                Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public object? Scalar(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        public Task<IReadOnlyList<ScriptSetInfo>> GetScriptSetsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<ScriptSetDeploymentData> GetDeploymentDataAsync(Guid id, CancellationToken token = default) => Task.FromResult(Data);
        public Task DownloadMaterialFileAsync(Guid id, Stream destination, CancellationToken token = default)
        {
            DownloadPath = ((FileStream)destination).Name;
            return Download?.Invoke(destination, token) ?? destination.WriteAsync(_materialBytes, token).AsTask();
        }
        public LoadedServerConfig LoadServerConfig(string directory) => new(_root, EngineType.GEE,
            new GeeConfig { SqliteDBName = "Mud2/game.db" }, new LauncherConfigBase { ResourcesDir = "Resource" }, new Setup(), []);
        public EngineType CheckEngineType(string directory) => EngineType.GEE;
        public Task<string> GetExternalIpAddressAsync() => throw new NotSupportedException();
        public bool CheckPorts(int[] ports) => throw new NotSupportedException();
        public string GetResourcesDirByGamePinyin(string name) => throw new NotSupportedException();
        public string GetLauncherName(ConfigStore store) => throw new NotSupportedException();
        public Task SaveConfigFileAsync(ConfigStore store) => throw new NotSupportedException();
        public void ApplyDefaultAuxiliarySettings(ConfigStore store) => throw new NotSupportedException();
        public Task GenerateCleanupScriptAsync(string directory) => throw new NotSupportedException();
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
