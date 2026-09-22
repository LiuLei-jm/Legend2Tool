using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;
using Serilog;
using System.IO;
using System.Security.Cryptography;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation
{
    internal sealed class MaterialDeployment
    {
        private readonly IScriptSetService _scriptSetService;
        private readonly ILogger _logger;

        internal MaterialDeployment(IScriptSetService scriptSetService, ILogger logger)
        {
            _scriptSetService = scriptSetService;
            _logger = logger;
        }

        internal List<MaterialFilePlan> CreateMaterialPlans(
            string serverDirectory,
            string resourcesDirectory,
            IReadOnlyList<MaterialFileInfo> materialFiles
        )
        {
            if (materialFiles.Count == 0)
            {
                return [];
            }

            if (string.IsNullOrWhiteSpace(resourcesDirectory))
            {
                throw new ScriptSetInstallationException(
                    "登录器 Resource 目录未配置，无法安装素材文件。"
                );
            }

            string pakPath = DeploymentPathResolver.ResolvePakPath(serverDirectory);
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

                string targetPath = DeploymentPathResolver.ResolveMaterialPath(
                    serverDirectory,
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

        internal static string CreateMaterialStagingDirectory()
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

        internal async Task StageMaterialFilesAsync(
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

        internal static void AssignMaterialBackupPaths(
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

        internal void DeleteMaterialStagingDirectory(string? stagingDirectory)
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

        internal static List<MaterialFilePlan> CreateMaterialFileRemovalPlans(
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

        internal static void ApplyMaterialFile(MaterialFilePlan plan)
        {
            DeploymentFileStore.CopyFileAtomically(plan.StagedPath, plan.TargetPath);
        }

        internal static void RemoveMaterialFile(MaterialFilePlan plan)
        {
            File.Delete(plan.TargetPath);
        }
    }
}
