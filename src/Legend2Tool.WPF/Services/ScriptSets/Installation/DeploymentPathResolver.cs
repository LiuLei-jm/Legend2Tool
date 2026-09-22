using System.IO;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation
{
    internal static class DeploymentPathResolver
    {
        private const string LauncherDirectoryName = "登录器";
        private const string PakFileName = "pak.txt";
        private const string PatchDirectoryName = "补丁文件夹";

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

        internal static string ResolvePakPath(string serverDirectory) => Path.Combine(
            serverDirectory,
            LauncherDirectoryName,
            PakFileName
        );
    }
}
