using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ScriptSets.Installation;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Database;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;
using Legend2Tool.WPF.State;
using Serilog;
using System.IO;
using System.Text;

namespace Legend2Tool.WPF.Services.ScriptSets
{
    public sealed class ScriptSetInstallationService : IScriptSetInstallationService
    {
        private readonly IScriptSetService _scriptSetService;
        private readonly ConfigStore _configStore;
        private readonly ScriptFileDeployment _scripts;
        private readonly MaterialDeployment _materials;
        private readonly PakFileEditor _pak;
        private readonly ILogger _logger;

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
            var files = new DeploymentFileStore(encodingService);
            _scripts = new ScriptFileDeployment(files);
            _materials = new MaterialDeployment(scriptSetService, logger);
            _pak = new PakFileEditor(files);
            _logger = logger;
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
            List<MaterialFilePlan> materialPlans = _materials.CreateMaterialPlans(
                _configStore.ServerDirectory,
                _configStore.LauncherConfig.ResourcesDir ?? string.Empty,
                deploymentData.MaterialFiles
            );
            string? stagingDirectory = null;
            try
            {
                if (materialPlans.Count > 0)
                {
                    stagingDirectory = MaterialDeployment.CreateMaterialStagingDirectory();
                    await _materials.StageMaterialFilesAsync(
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
                _materials.DeleteMaterialStagingDirectory(stagingDirectory);
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
            List<MaterialFilePlan> materialPlans = _materials.CreateMaterialPlans(
                _configStore.ServerDirectory,
                _configStore.LauncherConfig.ResourcesDir ?? string.Empty,
                deploymentData.MaterialFiles
            );
            string? stagingDirectory = null;
            try
            {
                if (materialPlans.Count > 0)
                {
                    stagingDirectory = MaterialDeployment.CreateMaterialStagingDirectory();
                    MaterialDeployment.AssignMaterialBackupPaths(materialPlans, stagingDirectory);
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
                _materials.DeleteMaterialStagingDirectory(stagingDirectory);
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
            List<ScriptFilePlan> scriptPlans = _scripts.CreateScriptPlans(
                _configStore.ServerDirectory,
                scriptSet,
                deploymentData.ScriptFiles
            );
            List<DatabaseRowPlan> databasePlans = ScriptSetDatabaseDeployment.CreateDatabasePlans(
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
                ? ScriptSetDatabaseDeployment.ResolveDatabaseTarget(
                    _configStore.ServerDirectory,
                    _configStore.EngineType,
                    _configStore.M2Config
                )
                : null;
            if (databaseTarget is not null)
            {
                ScriptSetDatabaseDeployment.ValidateDatabasePlans(databaseTarget, databasePlans);
            }

            Dictionary<string, byte[]?> snapshots = DeploymentFileStore.CaptureFileSnapshots(scriptPlans);
            List<DeploymentFileStore.MaterialFileSnapshot> materialSnapshots =
                DeploymentFileStore.CaptureMaterialFileSnapshots(materialPlans);
            if (materialPlans.Count > 0)
            {
                string pakPath = DeploymentPathResolver.ResolvePakPath(_configStore.ServerDirectory);
                snapshots[pakPath] = File.ReadAllBytes(pakPath);
            }
            try
            {
                foreach (ScriptFilePlan plan in scriptPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _scripts.ApplyScriptFile(plan);
                }

                foreach (MaterialFilePlan plan in materialPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MaterialDeployment.ApplyMaterialFile(plan);
                }
                if (materialPlans.Count > 0)
                {
                    _pak.AppendPakEntries(
                        DeploymentPathResolver.ResolvePakPath(_configStore.ServerDirectory),
                        materialPlans
                    );
                }

                // Commit the database last so earlier file changes can be restored on failure.
                if (databaseTarget is not null)
                {
                    ScriptSetDatabaseDeployment.InsertDatabaseRows(
                        databaseTarget,
                        databasePlans,
                        cancellationToken
                    );
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
                    DeploymentFileStore.RestoreInstallationSnapshots(snapshots, materialSnapshots);
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
            List<ScriptFilePlan> scriptPlans = _scripts.CreateScriptPlans(
                _configStore.ServerDirectory,
                scriptSet,
                deploymentData.ScriptFiles
            );
            List<DatabaseRowPlan> databasePlans = ScriptSetDatabaseDeployment.CreateDatabasePlans(
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
                ? ScriptSetDatabaseDeployment.ResolveDatabaseTarget(
                    _configStore.ServerDirectory,
                    _configStore.EngineType,
                    _configStore.M2Config
                )
                : null;
            if (databaseTarget is not null)
            {
                ScriptSetDatabaseDeployment.ValidateDatabasePlans(databaseTarget, databasePlans);
                ScriptSetDatabaseDeployment.ValidateDatabaseRemovalPlans(databasePlans);
            }

            List<ScriptFileRemovalPlan> fileRemovalPlans = _scripts.CreateFileRemovalPlans(
                scriptPlans,
                cancellationToken
            );
            List<MaterialFilePlan> materialRemovalPlans =
                MaterialDeployment.CreateMaterialFileRemovalPlans(materialPlans, cancellationToken);
            Dictionary<string, byte[]?> snapshots = DeploymentFileStore.CaptureFileSnapshots(
                fileRemovalPlans
            );
            List<DeploymentFileStore.MaterialFileSnapshot> materialSnapshots =
                DeploymentFileStore.CaptureMaterialFileSnapshots(materialRemovalPlans);
            if (materialPlans.Count > 0)
            {
                string pakPath = DeploymentPathResolver.ResolvePakPath(_configStore.ServerDirectory);
                snapshots[pakPath] = File.ReadAllBytes(pakPath);
            }
            try
            {
                foreach (ScriptFileRemovalPlan plan in fileRemovalPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScriptFileDeployment.ApplyFileRemoval(plan);
                }

                foreach (MaterialFilePlan plan in materialRemovalPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MaterialDeployment.RemoveMaterialFile(plan);
                }
                if (materialPlans.Count > 0)
                {
                    _pak.RemovePakEntries(
                        DeploymentPathResolver.ResolvePakPath(_configStore.ServerDirectory),
                        materialPlans
                    );
                }

                // Removal uses the same database-last boundary as installation.
                int removedDatabaseRows = databaseTarget is null
                    ? 0
                    : ScriptSetDatabaseDeployment.RemoveDatabaseRows(
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
                    DeploymentFileStore.RestoreInstallationSnapshots(snapshots, materialSnapshots);
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
    }
}
