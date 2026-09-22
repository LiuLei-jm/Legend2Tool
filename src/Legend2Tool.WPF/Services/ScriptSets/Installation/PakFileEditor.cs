using Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;
using System.Text;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation
{
    internal sealed class PakFileEditor
    {
        private readonly DeploymentFileStore _files;

        internal PakFileEditor(DeploymentFileStore files)
        {
            _files = files;
        }

        internal void AppendPakEntries(
            string pakPath,
            IReadOnlyList<MaterialFilePlan> plans
        )
        {
            DeploymentFileStore.TextFileState state = _files.ReadTextFileState(pakPath);
            Encoding strictEncoding = DeploymentFileStore.CreateStrictEncoding(state.Encoding);
            string content = strictEncoding.GetString(
                state.Bytes,
                state.Preamble.Length,
                state.Bytes.Length - state.Preamble.Length
            );
            string newLine = DeploymentFileStore.DetectNewLine(content);
            var output = new StringBuilder(content);
            if (output.Length > 0 && !DeploymentFileStore.EndsWithNewLine(content))
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

            DeploymentFileStore.WriteBytesAtomically(
                pakPath,
                DeploymentFileStore.EncodeWholeContent(output.ToString(), state.Encoding, state.Preamble)
            );
        }

        internal void RemovePakEntries(
            string pakPath,
            IReadOnlyList<MaterialFilePlan> plans
        )
        {
            DeploymentFileStore.TextFileState state = _files.ReadTextFileState(pakPath);
            Encoding strictEncoding = DeploymentFileStore.CreateStrictEncoding(state.Encoding);
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

            DeploymentFileStore.WriteBytesAtomically(
                pakPath,
                DeploymentFileStore.EncodeWholeContent(output, state.Encoding, state.Preamble)
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
    }
}
