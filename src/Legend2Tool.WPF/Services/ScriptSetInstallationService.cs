using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.State;
using Microsoft.Data.Sqlite;
using Serilog;
using SQLitePCL;
using System.Data.OleDb;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlueConfig = Legend2Tool.WPF.Models.M2Config.M2Config.BLUEConfig;
using GeeConfig = Legend2Tool.WPF.Models.M2Config.M2Config.GEEConfig;
using GomConfig = Legend2Tool.WPF.Models.M2Config.M2Config.GOMConfig;

namespace Legend2Tool.WPF.Services
{
    public sealed class ScriptSetInstallationService : IScriptSetInstallationService
    {
        private const string ScriptStartMarker = ";---脚本插入---";
        private const string ScriptEndMarker = ";---插入结束---";
        private const string DatabaseIndexColumn = "Idx";
        private const string LauncherDirectoryName = "登录器";
        private const string PatchDirectoryName = "补丁文件夹";
        private const string PakFileName = "pak.txt";

        private readonly IScriptSetService _scriptSetService;
        private readonly ConfigStore _configStore;
        private readonly IEncodingService _encodingService;
        private readonly ILogger _logger;
        private readonly Encoding _defaultScriptEncoding;

        public ScriptSetInstallationService(
            IScriptSetService scriptSetService,
            ConfigStore configStore,
            IEncodingService encodingService,
            ILogger logger
        )
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _scriptSetService = scriptSetService;
            _configStore = configStore;
            _encodingService = encodingService;
            _logger = logger;
            _defaultScriptEncoding = Encoding.GetEncoding("GB18030");
        }

        public async Task<ScriptSetInstallationResult> InstallAsync(
            ScriptSetInfo scriptSet,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(scriptSet);
            ValidateServerDirectory();

            ScriptSetDeploymentData deploymentData =
                await _scriptSetService.GetDeploymentDataAsync(
                    scriptSet.Id,
                    cancellationToken
                );
            List<MaterialFilePlan> materialPlans = CreateMaterialPlans(
                deploymentData.MaterialFiles
            );
            string? stagingDirectory = null;
            try
            {
                if (materialPlans.Count > 0)
                {
                    stagingDirectory = CreateMaterialStagingDirectory();
                    await StageMaterialFilesAsync(
                        materialPlans,
                        stagingDirectory,
                        cancellationToken
                    );
                }

                return await Task.Run(
                    () => InstallLocal(
                        scriptSet,
                        deploymentData,
                        materialPlans,
                        cancellationToken
                    ),
                    cancellationToken
                );
            }
            finally
            {
                DeleteMaterialStagingDirectory(stagingDirectory);
            }
        }

        public async Task<ScriptSetRemovalResult> RemoveAsync(
            ScriptSetInfo scriptSet,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(scriptSet);
            ValidateServerDirectory();

            ScriptSetDeploymentData deploymentData =
                await _scriptSetService.GetDeploymentDataAsync(
                    scriptSet.Id,
                    cancellationToken
                );
            List<MaterialFilePlan> materialPlans = CreateMaterialPlans(
                deploymentData.MaterialFiles
            );
            string? stagingDirectory = null;
            try
            {
                if (materialPlans.Count > 0)
                {
                    stagingDirectory = CreateMaterialStagingDirectory();
                    AssignMaterialBackupPaths(materialPlans, stagingDirectory);
                }
                return await Task.Run(
                    () => RemoveLocal(
                        scriptSet,
                        deploymentData,
                        materialPlans,
                        cancellationToken
                    ),
                    cancellationToken
                );
            }
            finally
            {
                DeleteMaterialStagingDirectory(stagingDirectory);
            }
        }

        private ScriptSetInstallationResult InstallLocal(
            ScriptSetInfo scriptSet,
            ScriptSetDeploymentData deploymentData,
            IReadOnlyList<MaterialFilePlan> materialPlans,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ScriptFilePlan> scriptPlans = CreateScriptPlans(
                scriptSet,
                deploymentData.ScriptFiles
            );
            List<DatabaseRowPlan> databasePlans = CreateDatabasePlans(
                deploymentData.DatabaseRows
            );
            if (
                scriptPlans.Count == 0
                && databasePlans.Count == 0
                && materialPlans.Count == 0
            )
            {
                throw new ScriptSetInstallationException(
                    $"脚本套“{scriptSet.Name}”没有可插入的脚本、数据库数据或素材文件。"
                );
            }
            DatabaseTarget? databaseTarget = databasePlans.Count > 0
                ? ResolveDatabaseTarget()
                : null;
            if (databaseTarget is not null)
            {
                ValidateDatabasePlans(databaseTarget, databasePlans);
            }

            Dictionary<string, byte[]?> snapshots = CaptureFileSnapshots(scriptPlans);
            List<MaterialFileSnapshot> materialSnapshots =
                CaptureMaterialFileSnapshots(materialPlans);
            if (materialPlans.Count > 0)
            {
                string pakPath = ResolvePakPath();
                snapshots[pakPath] = File.ReadAllBytes(pakPath);
            }
            try
            {
                foreach (ScriptFilePlan plan in scriptPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyScriptFile(plan);
                }

                foreach (MaterialFilePlan plan in materialPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyMaterialFile(plan);
                }
                if (materialPlans.Count > 0)
                {
                    AppendPakEntries(ResolvePakPath(), materialPlans);
                }

                if (databaseTarget is not null)
                {
                    InsertDatabaseRows(databaseTarget, databasePlans, cancellationToken);
                }

                return new ScriptSetInstallationResult(
                    scriptPlans.Count,
                    databasePlans.Count,
                    materialPlans.Count
                );
            }
            catch (Exception ex)
            {
                try
                {
                    RestoreInstallationSnapshots(snapshots, materialSnapshots);
                }
                catch (Exception rollbackException)
                {
                    _logger.Error(
                        rollbackException,
                        "脚本套 {ScriptSetId} 安装失败后回滚部署文件失败",
                        scriptSet.Id
                    );
                    throw new ScriptSetInstallationException(
                        "插入失败，且部分脚本文件无法自动恢复，请立即检查服务端文件。",
                        new AggregateException(ex, rollbackException)
                    );
                }

                if (ex is OperationCanceledException)
                {
                    throw;
                }
                if (ex is ScriptSetInstallationException)
                {
                    throw;
                }

                throw new ScriptSetInstallationException(
                    $"插入脚本套失败：{ex.Message}",
                    ex
                );
            }
        }

        private ScriptSetRemovalResult RemoveLocal(
            ScriptSetInfo scriptSet,
            ScriptSetDeploymentData deploymentData,
            IReadOnlyList<MaterialFilePlan> materialPlans,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ScriptFilePlan> scriptPlans = CreateScriptPlans(
                scriptSet,
                deploymentData.ScriptFiles
            );
            List<DatabaseRowPlan> databasePlans = CreateDatabasePlans(
                deploymentData.DatabaseRows
            );
            if (
                scriptPlans.Count == 0
                && databasePlans.Count == 0
                && materialPlans.Count == 0
            )
            {
                throw new ScriptSetInstallationException(
                    $"脚本套“{scriptSet.Name}”没有可删除的脚本、数据库数据或素材文件。"
                );
            }

            DatabaseTarget? databaseTarget = databasePlans.Count > 0
                ? ResolveDatabaseTarget()
                : null;
            if (databaseTarget is not null)
            {
                ValidateDatabasePlans(databaseTarget, databasePlans);
                ValidateDatabaseRemovalPlans(databasePlans);
            }

            List<ScriptFileRemovalPlan> fileRemovalPlans = CreateFileRemovalPlans(
                scriptPlans,
                cancellationToken
            );
            List<MaterialFilePlan> materialRemovalPlans =
                CreateMaterialFileRemovalPlans(materialPlans, cancellationToken);
            Dictionary<string, byte[]?> snapshots = CaptureFileSnapshots(
                fileRemovalPlans
            );
            List<MaterialFileSnapshot> materialSnapshots =
                CaptureMaterialFileSnapshots(materialRemovalPlans);
            if (materialPlans.Count > 0)
            {
                string pakPath = ResolvePakPath();
                snapshots[pakPath] = File.ReadAllBytes(pakPath);
            }
            try
            {
                foreach (ScriptFileRemovalPlan plan in fileRemovalPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyFileRemoval(plan);
                }

                foreach (MaterialFilePlan plan in materialRemovalPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(plan.TargetPath);
                }
                if (materialPlans.Count > 0)
                {
                    RemovePakEntries(ResolvePakPath(), materialPlans);
                }

                int removedDatabaseRows = databaseTarget is null
                    ? 0
                    : RemoveDatabaseRows(
                        databaseTarget,
                        databasePlans,
                        cancellationToken
                    );
                return new ScriptSetRemovalResult(
                    fileRemovalPlans.Count,
                    removedDatabaseRows,
                    materialRemovalPlans.Count
                );
            }
            catch (Exception ex)
            {
                try
                {
                    RestoreInstallationSnapshots(snapshots, materialSnapshots);
                }
                catch (Exception rollbackException)
                {
                    _logger.Error(
                        rollbackException,
                        "脚本套 {ScriptSetId} 删除失败后回滚部署文件失败",
                        scriptSet.Id
                    );
                    throw new ScriptSetInstallationException(
                        "删除失败，且部分脚本文件无法自动恢复，请立即检查服务端文件。",
                        new AggregateException(ex, rollbackException)
                    );
                }

                if (ex is OperationCanceledException)
                {
                    throw;
                }
                if (ex is ScriptSetInstallationException)
                {
                    throw;
                }

                throw new ScriptSetInstallationException(
                    $"删除脚本套失败：{ex.Message}",
                    ex
                );
            }
        }

        private void ValidateServerDirectory()
        {
            if (string.IsNullOrWhiteSpace(_configStore.ServerDirectory))
            {
                throw new ScriptSetInstallationException("请先加载服务端目录。");
            }
            if (!Directory.Exists(_configStore.ServerDirectory))
            {
                throw new ScriptSetInstallationException(
                    $"服务端目录不存在：{_configStore.ServerDirectory}"
                );
            }
        }

        private List<ScriptFilePlan> CreateScriptPlans(
            ScriptSetInfo scriptSet,
            IReadOnlyList<ScriptFileInfo> scriptFiles
        )
        {
            var plans = new List<ScriptFilePlan>(scriptFiles.Count);
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptFileInfo scriptFile in scriptFiles)
            {
                string targetPath = ResolveScriptPath(
                    _configStore.ServerDirectory,
                    scriptFile.FilePath,
                    scriptFile.FileName
                );
                if (!targetPaths.Add(targetPath))
                {
                    throw new ScriptSetInstallationException(
                        $"脚本套中存在重复的目标文件：{targetPath}"
                    );
                }

                switch (scriptFile.Type)
                {
                    case ScriptFileType.Whole when scriptFile.WholeContent is null:
                        throw new ScriptSetInstallationException(
                            $"全量脚本“{scriptFile.FileName}”缺少脚本内容。"
                        );
                    case ScriptFileType.Partial when scriptFile.Segments is null
                        || scriptFile.Segments.Count == 0:
                        throw new ScriptSetInstallationException(
                            $"片段脚本“{scriptFile.FileName}”没有可插入的片段。"
                        );
                    case ScriptFileType.Partial:
                        foreach (ScriptSegmentInfo segment in scriptFile.Segments!)
                        {
                            _ = NormalizeTrigger(segment.TriggerField);
                        }
                        break;
                    case not ScriptFileType.Whole:
                        throw new ScriptSetInstallationException(
                            $"脚本“{scriptFile.FileName}”使用了不支持的脚本类型。"
                        );
                }

                plans.Add(new ScriptFilePlan(scriptSet.Id, targetPath, scriptFile));
            }

            return plans;
        }

        private List<MaterialFilePlan> CreateMaterialPlans(
            IReadOnlyList<MaterialFileInfo> materialFiles
        )
        {
            if (materialFiles.Count == 0)
            {
                return [];
            }

            string resourcesDirectory = _configStore.LauncherConfig.ResourcesDir
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resourcesDirectory))
            {
                throw new ScriptSetInstallationException(
                    "登录器 Resource 目录未配置，无法安装素材文件。"
                );
            }

            string pakPath = ResolvePakPath();
            if (!File.Exists(pakPath))
            {
                throw new ScriptSetInstallationException(
                    $"登录器 PAK 配置文件不存在：{pakPath}"
                );
            }

            var plans = new List<MaterialFilePlan>(materialFiles.Count);
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MaterialFileInfo materialFile in materialFiles)
            {
                if (materialFile.Id == Guid.Empty)
                {
                    throw new ScriptSetInstallationException("素材文件 ID 不能为空。");
                }
                if (materialFile.FileSize < 0)
                {
                    throw new ScriptSetInstallationException(
                        $"素材文件“{materialFile.FileName}”的文件大小无效。"
                    );
                }
                if (
                    !string.IsNullOrWhiteSpace(materialFile.Sha256)
                    && (
                        materialFile.Sha256.Length != 64
                        || materialFile.Sha256.Any(character => !Uri.IsHexDigit(character))
                    )
                )
                {
                    throw new ScriptSetInstallationException(
                        $"素材文件“{materialFile.FileName}”的 SHA-256 无效。"
                    );
                }
                if (
                    materialFile.Password?.Contains('\r') == true
                    || materialFile.Password?.Contains('\n') == true
                )
                {
                    throw new ScriptSetInstallationException(
                        $"素材文件“{materialFile.FileName}”的密码不能包含换行符。"
                    );
                }

                string targetPath = ResolveMaterialPath(
                    _configStore.ServerDirectory,
                    resourcesDirectory,
                    materialFile.TargetPath,
                    materialFile.FileName
                );
                if (!targetPaths.Add(targetPath))
                {
                    throw new ScriptSetInstallationException(
                        $"脚本套中存在重复的素材目标文件：{targetPath}"
                    );
                }
                plans.Add(new MaterialFilePlan(materialFile, targetPath, string.Empty));
            }
            return plans;
        }

        internal static string ResolveMaterialPath(
            string serverDirectory,
            string resourcesDirectory,
            string relativeDirectory,
            string fileName
        )
        {
            if (string.IsNullOrWhiteSpace(serverDirectory))
            {
                throw new ScriptSetInstallationException("服务端目录不能为空。");
            }
            if (string.IsNullOrWhiteSpace(resourcesDirectory))
            {
                throw new ScriptSetInstallationException("Resource 目录不能为空。");
            }
            if (Path.IsPathRooted(resourcesDirectory))
            {
                throw new ScriptSetInstallationException(
                    $"Resource 目录必须是相对路径：{resourcesDirectory}"
                );
            }
            if (
                string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
                || Path.IsPathRooted(fileName)
            )
            {
                throw new ScriptSetInstallationException($"素材文件名无效：{fileName}");
            }
            if (Path.IsPathRooted(relativeDirectory ?? string.Empty))
            {
                throw new ScriptSetInstallationException(
                    $"素材部署路径必须是相对路径：{relativeDirectory}"
                );
            }

            string patchRoot = Path.GetFullPath(
                Path.Combine(serverDirectory, LauncherDirectoryName, PatchDirectoryName)
            ).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string resourceRoot = ResolveContainedDirectory(
                patchRoot,
                resourcesDirectory,
                "Resource 目录"
            );
            string targetDirectory = ResolveContainedDirectory(
                resourceRoot,
                relativeDirectory ?? string.Empty,
                "素材部署路径"
            );
            string targetPath = Path.GetFullPath(Path.Combine(targetDirectory, fileName));
            string requiredPrefix = resourceRoot + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ScriptSetInstallationException(
                    $"素材目标路径超出了 Resource 目录：{targetPath}"
                );
            }
            return targetPath;
        }

        private static string ResolveContainedDirectory(
            string rootDirectory,
            string relativeDirectory,
            string displayName
        )
        {
            string rootPath = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetPath = Path.GetFullPath(
                Path.Combine(
                    rootPath,
                    relativeDirectory.Replace('/', Path.DirectorySeparatorChar)
                )
            ).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (
                !string.Equals(targetPath, rootPath, StringComparison.OrdinalIgnoreCase)
                && !targetPath.StartsWith(
                    rootPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new ScriptSetInstallationException(
                    $"{displayName}超出了允许的目录：{relativeDirectory}"
                );
            }
            return targetPath;
        }

        private string ResolvePakPath() => Path.Combine(
            _configStore.ServerDirectory,
            LauncherDirectoryName,
            PakFileName
        );

        private static string CreateMaterialStagingDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "Legend2Tool",
                "ScriptSetMaterials",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(directory);
            return directory;
        }

        private async Task StageMaterialFilesAsync(
            List<MaterialFilePlan> plans,
            string stagingDirectory,
            CancellationToken cancellationToken
        )
        {
            for (int index = 0; index < plans.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MaterialFilePlan plan = plans[index];
                string stagedPath = Path.Combine(
                    stagingDirectory,
                    $"{index:D4}-{plan.MaterialFile.Id:N}.download"
                );
                await using (
                    var destination = new FileStream(
                        stagedPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        useAsync: true
                    )
                )
                {
                    await _scriptSetService.DownloadMaterialFileAsync(
                        plan.MaterialFile.Id,
                        destination,
                        cancellationToken
                    );
                }

                ValidateStagedMaterialFile(plan.MaterialFile, stagedPath);
                plans[index] = plan with { StagedPath = stagedPath };
            }
        }

        private static void AssignMaterialBackupPaths(
            List<MaterialFilePlan> plans,
            string stagingDirectory
        )
        {
            for (int index = 0; index < plans.Count; index++)
            {
                MaterialFilePlan plan = plans[index];
                plans[index] = plan with
                {
                    StagedPath = Path.Combine(
                        stagingDirectory,
                        $"{index:D4}-{plan.MaterialFile.Id:N}.removal"
                    )
                };
            }
        }

        private static void ValidateStagedMaterialFile(
            MaterialFileInfo materialFile,
            string stagedPath
        )
        {
            long actualSize = new FileInfo(stagedPath).Length;
            if (actualSize != materialFile.FileSize)
            {
                throw new ScriptSetInstallationException(
                    $"素材文件“{materialFile.FileName}”大小校验失败：期望 {materialFile.FileSize} 字节，实际 {actualSize} 字节。"
                );
            }
            if (string.IsNullOrWhiteSpace(materialFile.Sha256))
            {
                return;
            }

            using FileStream stream = File.OpenRead(stagedPath);
            string actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actualSha256.Equals(materialFile.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ScriptSetInstallationException(
                    $"素材文件“{materialFile.FileName}”的 SHA-256 校验失败。"
                );
            }
        }

        private void DeleteMaterialStagingDirectory(string? stagingDirectory)
        {
            if (string.IsNullOrWhiteSpace(stagingDirectory))
            {
                return;
            }
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "清理脚本套素材临时目录失败：{StagingDirectory}",
                    stagingDirectory
                );
            }
        }

        internal static string ResolveScriptPath(
            string serverDirectory,
            string relativeDirectory,
            string fileName
        )
        {
            if (string.IsNullOrWhiteSpace(serverDirectory))
            {
                throw new ScriptSetInstallationException("服务端目录不能为空。");
            }
            if (string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
                || Path.IsPathRooted(fileName))
            {
                throw new ScriptSetInstallationException($"脚本文件名无效：{fileName}");
            }
            if (Path.IsPathRooted(relativeDirectory))
            {
                throw new ScriptSetInstallationException(
                    $"脚本路径必须是服务端内的相对路径：{relativeDirectory}"
                );
            }

            string rootPath = Path.GetFullPath(serverDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetPath = Path.GetFullPath(
                Path.Combine(
                    rootPath,
                    (relativeDirectory ?? string.Empty).Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    ),
                    fileName
                )
            );
            string requiredPrefix = rootPath + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ScriptSetInstallationException(
                    $"脚本目标路径超出了服务端目录：{targetPath}"
                );
            }

            return targetPath;
        }

        private static List<DatabaseRowPlan> CreateDatabasePlans(
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

        private Dictionary<string, byte[]?> CaptureFileSnapshots(
            IEnumerable<ScriptFilePlan> plans
        )
        {
            var snapshots = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptFilePlan plan in plans)
            {
                snapshots[plan.TargetPath] = File.Exists(plan.TargetPath)
                    ? File.ReadAllBytes(plan.TargetPath)
                    : null;
            }
            return snapshots;
        }

        private static Dictionary<string, byte[]?> CaptureFileSnapshots(
            IEnumerable<ScriptFileRemovalPlan> plans
        )
        {
            var snapshots = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptFileRemovalPlan plan in plans)
            {
                snapshots[plan.TargetPath] = File.Exists(plan.TargetPath)
                    ? File.ReadAllBytes(plan.TargetPath)
                    : null;
            }
            return snapshots;
        }

        private static List<MaterialFileSnapshot> CaptureMaterialFileSnapshots(
            IReadOnlyList<MaterialFilePlan> plans
        )
        {
            var snapshots = new List<MaterialFileSnapshot>(plans.Count);
            foreach (MaterialFilePlan plan in plans)
            {
                string? backupPath = null;
                if (File.Exists(plan.TargetPath))
                {
                    backupPath = plan.StagedPath + ".original";
                    File.Copy(plan.TargetPath, backupPath, overwrite: false);
                }
                snapshots.Add(new MaterialFileSnapshot(plan.TargetPath, backupPath));
            }
            return snapshots;
        }

        private List<ScriptFileRemovalPlan> CreateFileRemovalPlans(
            IEnumerable<ScriptFilePlan> scriptPlans,
            CancellationToken cancellationToken
        )
        {
            var removalPlans = new List<ScriptFileRemovalPlan>();
            foreach (ScriptFilePlan plan in scriptPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(plan.TargetPath))
                {
                    continue;
                }

                TextFileState state = ReadTextFileState(plan.TargetPath);
                if (plan.ScriptFile.Type == ScriptFileType.Whole)
                {
                    byte[] expectedBytes = EncodeWholeContent(
                        plan.ScriptFile.WholeContent!,
                        state.Encoding,
                        state.Preamble
                    );
                    if (!state.Bytes.AsSpan().SequenceEqual(expectedBytes))
                    {
                        throw new ScriptSetInstallationException(
                            $"全量脚本“{plan.ScriptFile.FileName}”已被修改，为避免删除非脚本套内容，已停止删除。"
                        );
                    }
                    removalPlans.Add(new ScriptFileRemovalPlan(plan.TargetPath, null));
                    continue;
                }

                byte[] outputBytes = RemoveInsertedSegments(
                    state.Bytes,
                    state.Encoding,
                    state.Preamble.Length,
                    plan.ScriptSetId,
                    plan.ScriptFile.FileName
                );
                if (!state.Bytes.AsSpan().SequenceEqual(outputBytes))
                {
                    removalPlans.Add(
                        new ScriptFileRemovalPlan(plan.TargetPath, outputBytes)
                    );
                }
            }
            return removalPlans;
        }

        private static List<MaterialFilePlan> CreateMaterialFileRemovalPlans(
            IEnumerable<MaterialFilePlan> materialPlans,
            CancellationToken cancellationToken
        )
        {
            var removalPlans = new List<MaterialFilePlan>();
            foreach (MaterialFilePlan plan in materialPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(plan.TargetPath))
                {
                    continue;
                }

                ValidateStagedMaterialFile(plan.MaterialFile, plan.TargetPath);
                removalPlans.Add(plan);
            }
            return removalPlans;
        }

        private static void ApplyFileRemoval(ScriptFileRemovalPlan plan)
        {
            if (plan.OutputBytes is null)
            {
                File.Delete(plan.TargetPath);
                return;
            }

            WriteBytesAtomically(plan.TargetPath, plan.OutputBytes);
        }

        private void ApplyScriptFile(ScriptFilePlan plan)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(plan.TargetPath)
                    ?? throw new ScriptSetInstallationException("脚本目标目录无效。")
            );
            TextFileState state = ReadTextFileState(plan.TargetPath);
            byte[] outputBytes = plan.ScriptFile.Type switch
            {
                ScriptFileType.Whole => EncodeWholeContent(
                    plan.ScriptFile.WholeContent!,
                    state.Encoding,
                    state.Preamble
                ),
                ScriptFileType.Partial => InjectSegments(
                    state.Bytes,
                    state.Encoding,
                    state.Preamble.Length,
                    plan.ScriptSetId,
                    plan.ScriptFile
                ),
                _ => throw new ScriptSetInstallationException("不支持的脚本类型。")
            };
            WriteBytesAtomically(plan.TargetPath, outputBytes);
        }

        private static void ApplyMaterialFile(MaterialFilePlan plan)
        {
            CopyFileAtomically(plan.StagedPath, plan.TargetPath);
        }

        private void AppendPakEntries(
            string pakPath,
            IReadOnlyList<MaterialFilePlan> plans
        )
        {
            TextFileState state = ReadTextFileState(pakPath);
            Encoding strictEncoding = CreateStrictEncoding(state.Encoding);
            string content = strictEncoding.GetString(
                state.Bytes,
                state.Preamble.Length,
                state.Bytes.Length - state.Preamble.Length
            );
            string newLine = DetectNewLine(content);
            var output = new StringBuilder(content);
            if (output.Length > 0 && !EndsWithNewLine(content))
            {
                output.Append(newLine);
            }
            foreach (MaterialFilePlan plan in plans)
            {
                output.Append(plan.TargetPath);
                output.Append('|');
                output.Append(plan.MaterialFile.Password ?? string.Empty);
                output.Append(newLine);
            }

            WriteBytesAtomically(
                pakPath,
                EncodeWholeContent(output.ToString(), state.Encoding, state.Preamble)
            );
        }

        private void RemovePakEntries(
            string pakPath,
            IReadOnlyList<MaterialFilePlan> plans
        )
        {
            TextFileState state = ReadTextFileState(pakPath);
            Encoding strictEncoding = CreateStrictEncoding(state.Encoding);
            string content = strictEncoding.GetString(
                state.Bytes,
                state.Preamble.Length,
                state.Bytes.Length - state.Preamble.Length
            );
            var targetPaths = new HashSet<string>(
                plans.Select(plan => plan.TargetPath),
                StringComparer.OrdinalIgnoreCase
            );
            string output = RemovePakEntryLines(content, targetPaths);
            if (string.Equals(output, content, StringComparison.Ordinal))
            {
                return;
            }

            WriteBytesAtomically(
                pakPath,
                EncodeWholeContent(output, state.Encoding, state.Preamble)
            );
        }

        private static string RemovePakEntryLines(
            string content,
            IReadOnlySet<string> targetPaths
        )
        {
            var output = new StringBuilder(content.Length);
            int position = 0;
            while (position < content.Length)
            {
                int lineEnd = position;
                while (
                    lineEnd < content.Length
                    && content[lineEnd] != '\r'
                    && content[lineEnd] != '\n'
                )
                {
                    lineEnd++;
                }

                int nextLine = lineEnd;
                if (nextLine < content.Length)
                {
                    if (
                        content[nextLine] == '\r'
                        && nextLine + 1 < content.Length
                        && content[nextLine + 1] == '\n'
                    )
                    {
                        nextLine += 2;
                    }
                    else
                    {
                        nextLine++;
                    }
                }

                ReadOnlySpan<char> line = content.AsSpan(position, lineEnd - position);
                int separatorIndex = line.IndexOf('|');
                bool remove = separatorIndex >= 0
                    && targetPaths.Contains(line[..separatorIndex].ToString());
                if (!remove)
                {
                    output.Append(content, position, nextLine - position);
                }
                position = nextLine;
            }
            return output.ToString();
        }

        private TextFileState ReadTextFileState(string path)
        {
            if (!File.Exists(path))
            {
                return new TextFileState([], _defaultScriptEncoding, []);
            }

            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = _encodingService.DetectFileEncodingResult(path).Encoding
                ?? _defaultScriptEncoding;
            byte[] preamble = encoding.GetPreamble();
            if (!bytes.AsSpan().StartsWith(preamble))
            {
                preamble = [];
            }
            return new TextFileState(bytes, encoding, preamble);
        }

        private static byte[] EncodeWholeContent(
            string content,
            Encoding encoding,
            byte[] preamble
        )
        {
            Encoding strictEncoding = CreateStrictEncoding(encoding);
            byte[] contentBytes = strictEncoding.GetBytes(content);
            if (preamble.Length == 0)
            {
                return contentBytes;
            }

            byte[] output = new byte[preamble.Length + contentBytes.Length];
            preamble.CopyTo(output, 0);
            contentBytes.CopyTo(output, preamble.Length);
            return output;
        }

        internal static byte[] InjectSegments(
            byte[] originalBytes,
            Encoding encoding,
            int preambleLength,
            Guid scriptSetId,
            ScriptFileInfo scriptFile
        )
        {
            byte[] output = originalBytes;
            Encoding strictEncoding = CreateStrictEncoding(encoding);
            IReadOnlyList<ScriptSegmentInfo> segments = scriptFile.Segments ?? [];
            List<IndexedSegment> indexedSegments = segments
                .Select((segment, index) => new IndexedSegment(
                    segment,
                    index,
                    NormalizeTrigger(segment.TriggerField)
                ))
                .ToList();
            IEnumerable<IndexedSegment> insertionOrder = indexedSegments
                .Where(item => item.Trigger == "#top")
                .Reverse()
                .Concat(
                    indexedSegments
                        .Where(item => item.Trigger is not ("#top" or "#bottom"))
                        .GroupBy(item => item.Trigger, StringComparer.OrdinalIgnoreCase)
                        .SelectMany(group => group.Reverse())
                )
                .Concat(indexedSegments.Where(item => item.Trigger == "#bottom"));

            foreach (IndexedSegment indexedSegment in insertionOrder)
            {
                ScriptSegmentInfo segment = indexedSegment.Segment;
                string content = encoding.GetString(
                    output,
                    preambleLength,
                    output.Length - preambleLength
                );
                string markerKey = segment.Id?.ToString("N")
                    ?? $"{scriptFile.Id:N}-{indexedSegment.Index}";
                string startMarker =
                    $"{ScriptStartMarker} ScriptSet={scriptSetId:N};Segment={markerKey}";
                string endMarker =
                    $"{ScriptEndMarker} ScriptSet={scriptSetId:N};Segment={markerKey}";
                bool containsStartMarker = content.Contains(
                    startMarker,
                    StringComparison.Ordinal
                );
                bool containsEndMarker = content.Contains(
                    endMarker,
                    StringComparison.Ordinal
                );
                if (containsStartMarker && containsEndMarker)
                {
                    continue;
                }
                if (containsStartMarker || containsEndMarker)
                {
                    throw new ScriptSetInstallationException(
                        $"脚本“{scriptFile.FileName}”中存在不完整的插入标识，已停止写入。"
                    );
                }

                string newLine = DetectNewLine(content);
                string block = CreateSegmentBlock(
                    startMarker,
                    endMarker,
                    segment.Content ?? string.Empty,
                    newLine
                );
                TextInsertion insertion = CreateTextInsertion(
                    content,
                    indexedSegment.Trigger,
                    block,
                    newLine
                );
                int byteIndex = preambleLength + strictEncoding.GetByteCount(
                    content.AsSpan(0, insertion.CharacterIndex)
                );
                byte[] insertedBytes = strictEncoding.GetBytes(insertion.Text);
                output = InsertBytes(output, byteIndex, insertedBytes);
            }

            return output;
        }

        internal static byte[] RemoveInsertedSegments(
            byte[] sourceBytes,
            Encoding encoding,
            int preambleLength,
            Guid scriptSetId,
            string fileName
        )
        {
            if (preambleLength < 0 || preambleLength > sourceBytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(preambleLength));
            }

            Encoding strictEncoding = CreateStrictEncoding(encoding);
            string content = strictEncoding.GetString(
                sourceBytes,
                preambleLength,
                sourceBytes.Length - preambleLength
            );
            string startPrefix =
                $"{ScriptStartMarker} ScriptSet={scriptSetId:N};Segment=";
            string endPrefix =
                $"{ScriptEndMarker} ScriptSet={scriptSetId:N};Segment=";
            List<TextRange> ranges = FindInsertedSegmentRanges(
                content,
                startPrefix,
                endPrefix,
                fileName
            );

            byte[] output = sourceBytes;
            foreach (TextRange range in ranges.OrderByDescending(range => range.Start))
            {
                int byteStart = preambleLength + strictEncoding.GetByteCount(
                    content.AsSpan(0, range.Start)
                );
                int byteEnd = preambleLength + strictEncoding.GetByteCount(
                    content.AsSpan(0, range.Start + range.Length)
                );
                output = RemoveBytes(output, byteStart, byteEnd - byteStart);
            }
            return output;
        }

        private static List<TextRange> FindInsertedSegmentRanges(
            string content,
            string startPrefix,
            string endPrefix,
            string fileName
        )
        {
            var ranges = new List<TextRange>();
            int? blockStart = null;
            string? markerKey = null;
            int lineStart = 0;
            while (lineStart <= content.Length)
            {
                int lineEnd = lineStart;
                while (lineEnd < content.Length && content[lineEnd] is not ('\r' or '\n'))
                {
                    lineEnd++;
                }

                ReadOnlySpan<char> line = content.AsSpan(lineStart, lineEnd - lineStart);
                if (line.StartsWith(startPrefix, StringComparison.Ordinal))
                {
                    if (blockStart is not null)
                    {
                        throw CreateIncompleteMarkerException(fileName);
                    }

                    markerKey = line[startPrefix.Length..].ToString();
                    if (markerKey.Length == 0)
                    {
                        throw CreateIncompleteMarkerException(fileName);
                    }
                    blockStart = lineStart;
                }
                else if (line.StartsWith(endPrefix, StringComparison.Ordinal))
                {
                    string endMarkerKey = line[endPrefix.Length..].ToString();
                    if (blockStart is null
                        || !string.Equals(
                            markerKey,
                            endMarkerKey,
                            StringComparison.Ordinal
                        ))
                    {
                        throw CreateIncompleteMarkerException(fileName);
                    }

                    ranges.Add(new TextRange(blockStart.Value, lineEnd - blockStart.Value));
                    blockStart = null;
                    markerKey = null;
                }

                if (lineEnd >= content.Length)
                {
                    break;
                }
                lineStart = lineEnd + 1;
                if (content[lineEnd] == '\r'
                    && lineStart < content.Length
                    && content[lineStart] == '\n')
                {
                    lineStart++;
                }
            }

            if (blockStart is not null)
            {
                throw CreateIncompleteMarkerException(fileName);
            }
            return ranges;
        }

        private static ScriptSetInstallationException CreateIncompleteMarkerException(
            string fileName
        ) => new(
            $"脚本“{fileName}”中存在不完整的插入标识，为避免删除原文件内容，已停止删除。"
        );

        internal static string NormalizeTrigger(string triggerField)
        {
            if (string.IsNullOrWhiteSpace(triggerField))
            {
                throw new ScriptSetInstallationException("脚本片段的触发标识不能为空。");
            }

            string trigger = triggerField.Trim();
            if (trigger.Contains('\r') || trigger.Contains('\n'))
            {
                throw new ScriptSetInstallationException("脚本片段的触发标识不能包含换行。");
            }
            if (trigger.Equals("#top", StringComparison.OrdinalIgnoreCase)
                || trigger.Equals("#bottom", StringComparison.OrdinalIgnoreCase))
            {
                return trigger.ToLowerInvariant();
            }

            bool startsWithBracket = trigger.StartsWith('[');
            bool endsWithBracket = trigger.EndsWith(']');
            if (startsWithBracket != endsWithBracket)
            {
                throw new ScriptSetInstallationException(
                    $"脚本片段的触发标识括号不完整：{triggerField}"
                );
            }

            string innerTrigger = startsWithBracket
                ? trigger[1..^1].Trim()
                : trigger;
            if (innerTrigger.Length == 0
                || innerTrigger.Contains('[')
                || innerTrigger.Contains(']'))
            {
                throw new ScriptSetInstallationException(
                    $"脚本片段的触发标识无效：{triggerField}"
                );
            }
            return $"[{innerTrigger}]";
        }

        private static TextInsertion CreateTextInsertion(
            string content,
            string trigger,
            string block,
            string newLine
        )
        {
            if (trigger == "#top")
            {
                return new TextInsertion(0, content.Length == 0 ? block : block + newLine);
            }
            if (trigger == "#bottom")
            {
                string separator = content.Length == 0 || EndsWithNewLine(content)
                    ? string.Empty
                    : newLine;
                return new TextInsertion(content.Length, separator + block);
            }

            if (TryFindTriggerLine(content, trigger, out int lineEnd, out int nextLineStart))
            {
                if (nextLineStart > lineEnd)
                {
                    return new TextInsertion(nextLineStart, block + newLine);
                }
                return new TextInsertion(lineEnd, newLine + block);
            }

            string prefix = content.Length == 0 || EndsWithNewLine(content)
                ? string.Empty
                : newLine;
            return new TextInsertion(
                content.Length,
                prefix + trigger + newLine + block
            );
        }

        private static bool TryFindTriggerLine(
            string content,
            string trigger,
            out int lineEnd,
            out int nextLineStart
        )
        {
            int triggerIndex = content.IndexOf(
                trigger,
                StringComparison.OrdinalIgnoreCase
            );
            if (triggerIndex < 0)
            {
                lineEnd = 0;
                nextLineStart = 0;
                return false;
            }

            lineEnd = triggerIndex + trigger.Length;
            while (lineEnd < content.Length && content[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            nextLineStart = lineEnd;
            if (nextLineStart < content.Length && content[nextLineStart] == '\r')
            {
                nextLineStart++;
            }
            if (nextLineStart < content.Length && content[nextLineStart] == '\n')
            {
                nextLineStart++;
            }
            return true;
        }

        private static string CreateSegmentBlock(
            string startMarker,
            string endMarker,
            string segmentContent,
            string newLine
        )
        {
            string normalizedContent = segmentContent
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace("\n", newLine, StringComparison.Ordinal);
            string contentSeparator = normalizedContent.Length == 0
                || EndsWithNewLine(normalizedContent)
                    ? string.Empty
                    : newLine;
            return startMarker
                + newLine
                + normalizedContent
                + contentSeparator
                + endMarker;
        }

        private static string DetectNewLine(string content)
        {
            int newLineIndex = content.IndexOfAny(['\r', '\n']);
            if (newLineIndex < 0)
            {
                return Environment.NewLine;
            }
            return content[newLineIndex] == '\r'
                && newLineIndex + 1 < content.Length
                && content[newLineIndex + 1] == '\n'
                    ? "\r\n"
                    : content[newLineIndex].ToString();
        }

        private static bool EndsWithNewLine(string content) =>
            content.EndsWith('\r') || content.EndsWith('\n');

        private static Encoding CreateStrictEncoding(Encoding encoding)
        {
            bool emitPreamble = encoding.GetPreamble().Length > 0;
            return encoding.CodePage switch
            {
                65001 => new UTF8Encoding(emitPreamble, true),
                1200 => new UnicodeEncoding(false, emitPreamble, true),
                1201 => new UnicodeEncoding(true, emitPreamble, true),
                12000 => new UTF32Encoding(false, emitPreamble, true),
                12001 => new UTF32Encoding(true, emitPreamble, true),
                _ => Encoding.GetEncoding(
                    encoding.CodePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback
                )
            };
        }

        private static byte[] InsertBytes(byte[] source, int index, byte[] insertedBytes)
        {
            byte[] output = new byte[source.Length + insertedBytes.Length];
            Buffer.BlockCopy(source, 0, output, 0, index);
            Buffer.BlockCopy(insertedBytes, 0, output, index, insertedBytes.Length);
            Buffer.BlockCopy(
                source,
                index,
                output,
                index + insertedBytes.Length,
                source.Length - index
            );
            return output;
        }

        private static byte[] RemoveBytes(byte[] source, int index, int length)
        {
            if (index < 0 || length < 0 || index > source.Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            byte[] output = new byte[source.Length - length];
            Buffer.BlockCopy(source, 0, output, 0, index);
            Buffer.BlockCopy(
                source,
                index + length,
                output,
                index,
                source.Length - index - length
            );
            return output;
        }

        private static void WriteBytesAtomically(string targetPath, byte[] content)
        {
            string directory = Path.GetDirectoryName(targetPath)
                ?? throw new ScriptSetInstallationException("脚本目标目录无效。");
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp"
            );
            try
            {
                File.WriteAllBytes(tempPath, content);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void CopyFileAtomically(string sourcePath, string targetPath)
        {
            string directory = Path.GetDirectoryName(targetPath)
                ?? throw new ScriptSetInstallationException("素材目标目录无效。");
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp"
            );
            try
            {
                File.Copy(sourcePath, tempPath, overwrite: false);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void RestoreInstallationSnapshots(
            IReadOnlyDictionary<string, byte[]?> fileSnapshots,
            IReadOnlyList<MaterialFileSnapshot> materialSnapshots
        )
        {
            var failures = new List<Exception>();
            foreach (MaterialFileSnapshot snapshot in materialSnapshots)
            {
                try
                {
                    if (snapshot.BackupPath is null)
                    {
                        if (File.Exists(snapshot.TargetPath))
                        {
                            File.Delete(snapshot.TargetPath);
                        }
                    }
                    else
                    {
                        CopyFileAtomically(snapshot.BackupPath, snapshot.TargetPath);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            try
            {
                RestoreFileSnapshots(fileSnapshots);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            if (failures.Count == 1)
            {
                throw failures[0];
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }

        private static void RestoreFileSnapshots(
            IReadOnlyDictionary<string, byte[]?> snapshots
        )
        {
            foreach ((string path, byte[]? content) in snapshots)
            {
                if (content is null)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                else
                {
                    WriteBytesAtomically(path, content);
                }
            }
        }

        private DatabaseTarget ResolveDatabaseTarget()
        {
            (string? configuredPath, DatabaseProvider provider) = _configStore.EngineType switch
            {
                EngineType.BLUE when _configStore.M2Config is BlueConfig config =>
                    (config.DataTableFile, DatabaseProvider.Sqlite),
                EngineType.GOM or EngineType.NEWGOM
                    when _configStore.M2Config is GomConfig config =>
                    (config.AccessFileName, DatabaseProvider.Access),
                EngineType.HGE when _configStore.M2Config is HGEConfig config =>
                    (config.SQLiteName, DatabaseProvider.Sqlite),
                EngineType.GEE or EngineType.GXX or EngineType.LF or EngineType.V8
                    when _configStore.M2Config is GeeConfig config =>
                    (config.SqliteDBName, DatabaseProvider.Sqlite),
                _ => throw new ScriptSetInstallationException(
                    $"当前引擎不支持数据库追加：{_configStore.EngineType}"
                )
            };

            string databasePath = ConfigPathResolver.ResolveServerPath(
                _configStore.ServerDirectory,
                configuredPath,
                "Mud2"
            );
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                throw new ScriptSetInstallationException(
                    $"没有找到当前引擎的数据库文件：{databasePath}"
                );
            }
            return new DatabaseTarget(databasePath, provider, _configStore.EngineType);
        }

        private static void ValidateDatabasePlans(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans
        )
        {
            if (target.Provider == DatabaseProvider.Access)
            {
                using var connection = new OleDbConnection(CreateAccessConnectionString(target.Path));
                connection.Open();
                ValidateAccessPlans(connection, target.EngineType, plans);
                return;
            }

            Batteries_V2.Init();
            using var sqliteConnection = new SqliteConnection(
                CreateSqliteConnectionString(target.Path)
            );
            sqliteConnection.Open();
            ValidateSqlitePlans(sqliteConnection, target.EngineType, plans);
        }

        private static void InsertDatabaseRows(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        )
        {
            if (target.Provider == DatabaseProvider.Access)
            {
                InsertAccessRows(target, plans, cancellationToken);
            }
            else
            {
                InsertSqliteRows(target, plans, cancellationToken);
            }
        }

        private static int RemoveDatabaseRows(
            DatabaseTarget target,
            IReadOnlyList<DatabaseRowPlan> plans,
            CancellationToken cancellationToken
        ) => target.Provider == DatabaseProvider.Access
            ? RemoveAccessRows(target, plans, cancellationToken)
            : RemoveSqliteRows(target, plans, cancellationToken);

        private static void ValidateDatabaseRemovalPlans(
            IEnumerable<DatabaseRowPlan> plans
        )
        {
            DatabaseRowPlan? unsafePlan = plans.FirstOrDefault(
                plan => !plan.Values.Keys.Any(
                    column => !column.Equals(
                        DatabaseIndexColumn,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
            if (unsafePlan is not null)
            {
                throw new ScriptSetInstallationException(
                    $"数据库数据“{unsafePlan.Name}”除 {DatabaseIndexColumn} 外没有可用于安全匹配的字段，已停止删除。"
                );
            }
        }

        private static void ValidateSqlitePlans(
            SqliteConnection connection,
            EngineType engineType,
            IReadOnlyList<DatabaseRowPlan> plans
        )
        {
            foreach (IGrouping<string, DatabaseRowPlan> group in plans.GroupBy(
                plan => GetTableName(engineType, plan.TableType),
                StringComparer.OrdinalIgnoreCase
            ))
            {
                HashSet<string> columns = GetSqliteColumns(connection, group.Key);
                ValidateColumns(group, group.Key, columns);
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

        private static void ValidateAccessPlans(
            OleDbConnection connection,
            EngineType engineType,
            IReadOnlyList<DatabaseRowPlan> plans
        )
        {
            foreach (IGrouping<string, DatabaseRowPlan> group in plans.GroupBy(
                plan => GetTableName(engineType, plan.TableType),
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
                ValidateColumns(group, group.Key, columns);
            }
        }

        private static void ValidateColumns(
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

        internal static void InsertSqliteRows(
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
                    string tableName = GetTableName(target.EngineType, plan.TableType);
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

        internal static int RemoveSqliteRows(
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
                    string tableName = GetTableName(target.EngineType, plan.TableType);
                    KeyValuePair<string, object?>[] matchValues = GetDatabaseMatchValues(
                        plan
                    );

                    using SqliteCommand findCommand = connection.CreateCommand();
                    findCommand.Transaction = transaction;
                    string whereClause = AddSqliteMatchParameters(
                        findCommand,
                        matchValues
                    );
                    findCommand.CommandText =
                        $"SELECT {QuoteSqliteIdentifier(DatabaseIndexColumn)}"
                        + $" FROM {QuoteSqliteIdentifier(tableName)}"
                        + $" WHERE {whereClause}"
                        + $" ORDER BY {QuoteSqliteIdentifier(DatabaseIndexColumn)} DESC LIMIT 1;";
                    object? index = findCommand.ExecuteScalar();
                    if (index is null || index is DBNull)
                    {
                        continue;
                    }

                    using SqliteCommand deleteCommand = connection.CreateCommand();
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText =
                        $"DELETE FROM {QuoteSqliteIdentifier(tableName)}"
                        + $" WHERE {QuoteSqliteIdentifier(DatabaseIndexColumn)} = $idx;";
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

        private static void InsertAccessRows(
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
                    string tableName = GetTableName(target.EngineType, plan.TableType);
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

        private static int RemoveAccessRows(
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
                    string tableName = GetTableName(target.EngineType, plan.TableType);
                    KeyValuePair<string, object?>[] matchValues = GetDatabaseMatchValues(
                        plan
                    );

                    using OleDbCommand findCommand = connection.CreateCommand();
                    findCommand.Transaction = transaction;
                    string whereClause = AddAccessMatchParameters(
                        findCommand,
                        matchValues
                    );
                    findCommand.CommandText =
                        $"SELECT TOP 1 {QuoteAccessIdentifier(DatabaseIndexColumn)}"
                        + $" FROM {QuoteAccessIdentifier(tableName)}"
                        + $" WHERE {whereClause}"
                        + $" ORDER BY {QuoteAccessIdentifier(DatabaseIndexColumn)} DESC";
                    object? index = findCommand.ExecuteScalar();
                    if (index is null || index is DBNull)
                    {
                        continue;
                    }

                    using OleDbCommand deleteCommand = connection.CreateCommand();
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText =
                        $"DELETE FROM {QuoteAccessIdentifier(tableName)}"
                        + $" WHERE {QuoteAccessIdentifier(DatabaseIndexColumn)} = ?";
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

        private static KeyValuePair<string, object?>[] GetDatabaseMatchValues(
            DatabaseRowPlan plan
        ) => plan.Values
            .Where(value => !value.Key.Equals(
                DatabaseIndexColumn,
                StringComparison.OrdinalIgnoreCase
            ))
            .ToArray();

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
                    $"SELECT MAX({QuoteSqliteIdentifier(DatabaseIndexColumn)}) FROM {QuoteSqliteIdentifier(tableName)};";
                nextIndex = GetNextIndex(command.ExecuteScalar(), tableName);
            }

            plan.Values[DatabaseIndexColumn] = nextIndex;
            nextIndexes[tableName] = checked(nextIndex + 1);
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
                    $"SELECT MAX({QuoteAccessIdentifier(DatabaseIndexColumn)}) FROM {QuoteAccessIdentifier(tableName)}";
                nextIndex = GetNextIndex(command.ExecuteScalar(), tableName);
            }

            plan.Values[DatabaseIndexColumn] = nextIndex;
            nextIndexes[tableName] = checked(nextIndex + 1);
        }

        private static long GetNextIndex(object? maximumValue, string tableName)
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

        private static string GetTableName(
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

        private static string CreateAccessConnectionString(string path) =>
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={path};Persist Security Info=False;";

        private static string CreateSqliteConnectionString(string path) =>
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();

        private static string QuoteSqliteIdentifier(string identifier) =>
            $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

        private static string QuoteAccessIdentifier(string identifier) =>
            $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

        private sealed record ScriptFilePlan(
            Guid ScriptSetId,
            string TargetPath,
            ScriptFileInfo ScriptFile
        );

        private sealed record ScriptFileRemovalPlan(
            string TargetPath,
            byte[]? OutputBytes
        );

        private sealed record MaterialFilePlan(
            MaterialFileInfo MaterialFile,
            string TargetPath,
            string StagedPath
        );

        private sealed record MaterialFileSnapshot(
            string TargetPath,
            string? BackupPath
        );

        private sealed record TextFileState(
            byte[] Bytes,
            Encoding Encoding,
            byte[] Preamble
        );

        private sealed record TextInsertion(int CharacterIndex, string Text);

        private sealed record TextRange(int Start, int Length);

        private sealed record IndexedSegment(
            ScriptSegmentInfo Segment,
            int Index,
            string Trigger
        );

        internal sealed record DatabaseRowPlan(
            GameDatabaseTableType TableType,
            string Name,
            Dictionary<string, object?> Values
        );

        internal sealed record DatabaseTarget(
            string Path,
            DatabaseProvider Provider,
            EngineType EngineType
        );

        internal enum DatabaseProvider
        {
            Sqlite,
            Access
        }
    }
}
