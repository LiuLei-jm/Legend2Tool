using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;
using System.IO;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation
{
    internal sealed class ScriptFileDeployment
    {
        private readonly DeploymentFileStore _files;

        internal ScriptFileDeployment(DeploymentFileStore files)
        {
            _files = files;
        }

        internal List<ScriptFilePlan> CreateScriptPlans(
            string serverDirectory,
            ScriptSetInfo scriptSet,
            IReadOnlyList<ScriptFileInfo> scriptFiles
        )
        {
            var plans = new List<ScriptFilePlan>(scriptFiles.Count);
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptFileInfo scriptFile in scriptFiles)
            {
                string targetPath = DeploymentPathResolver.ResolveScriptPath(
                    serverDirectory,
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
                            _ = ScriptSegmentEditor.NormalizeTrigger(segment.TriggerField);
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

        internal List<ScriptFileRemovalPlan> CreateFileRemovalPlans(
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

                DeploymentFileStore.TextFileState state = _files.ReadTextFileState(plan.TargetPath);
                if (plan.ScriptFile.Type == ScriptFileType.Whole)
                {
                    byte[] expectedBytes = DeploymentFileStore.EncodeWholeContent(
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

                byte[] outputBytes = ScriptSegmentEditor.RemoveInsertedSegments(
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

        internal static void ApplyFileRemoval(ScriptFileRemovalPlan plan)
        {
            if (plan.OutputBytes is null)
            {
                File.Delete(plan.TargetPath);
                return;
            }

            DeploymentFileStore.WriteBytesAtomically(plan.TargetPath, plan.OutputBytes);
        }

        internal void ApplyScriptFile(ScriptFilePlan plan)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(plan.TargetPath)
                    ?? throw new ScriptSetInstallationException("脚本目标目录无效。")
            );
            DeploymentFileStore.TextFileState state = _files.ReadTextFileState(plan.TargetPath);
            byte[] outputBytes = plan.ScriptFile.Type switch
            {
                ScriptFileType.Whole => DeploymentFileStore.EncodeWholeContent(
                    plan.ScriptFile.WholeContent!,
                    state.Encoding,
                    state.Preamble
                ),
                ScriptFileType.Partial => ScriptSegmentEditor.InjectSegments(
                    state.Bytes,
                    state.Encoding,
                    state.Preamble.Length,
                    plan.ScriptSetId,
                    plan.ScriptFile
                ),
                _ => throw new ScriptSetInstallationException("不支持的脚本类型。")
            };
            DeploymentFileStore.WriteBytesAtomically(plan.TargetPath, outputBytes);
        }
    }
}
