using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;
using System.IO;
using System.Text;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation
{
    internal sealed class DeploymentFileStore
    {
        private readonly IEncodingService _encodingService;
        private readonly Encoding _defaultScriptEncoding;

        internal DeploymentFileStore(IEncodingService encodingService)
        {
            _encodingService = encodingService;
            _defaultScriptEncoding = Encoding.GetEncoding("GB18030");
        }

        internal static Dictionary<string, byte[]?> CaptureFileSnapshots(
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

        internal static Dictionary<string, byte[]?> CaptureFileSnapshots(
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

        internal static List<MaterialFileSnapshot> CaptureMaterialFileSnapshots(
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

        internal TextFileState ReadTextFileState(string path)
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

        internal static byte[] EncodeWholeContent(
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

        internal static void WriteBytesAtomically(string targetPath, byte[] content)
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

        internal static void CopyFileAtomically(string sourcePath, string targetPath)
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

        internal static void RestoreInstallationSnapshots(
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

        internal static string DetectNewLine(string content)
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

        internal static bool EndsWithNewLine(string content) =>
            content.EndsWith('\r') || content.EndsWith('\n');

        internal static Encoding CreateStrictEncoding(Encoding encoding)
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

        internal sealed record MaterialFileSnapshot(
            string TargetPath,
            string? BackupPath
        );

        internal sealed record TextFileState(
            byte[] Bytes,
            Encoding Encoding,
            byte[] Preamble
        );
    }
}
