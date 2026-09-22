using Legend2Tool.WPF.Models.ScriptSets;
using System.Text;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation
{
    internal static class ScriptSegmentEditor
    {
        private const string ScriptStartMarker = ";---脚本插入---";
        private const string ScriptEndMarker = ";---插入结束---";

        internal static byte[] InjectSegments(
            byte[] originalBytes,
            Encoding encoding,
            int preambleLength,
            Guid scriptSetId,
            ScriptFileInfo scriptFile
        )
        {
            byte[] output = originalBytes;
            Encoding strictEncoding = DeploymentFileStore.CreateStrictEncoding(encoding);
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

                string newLine = DeploymentFileStore.DetectNewLine(content);
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

            Encoding strictEncoding = DeploymentFileStore.CreateStrictEncoding(encoding);
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
                string separator = content.Length == 0 || DeploymentFileStore.EndsWithNewLine(content)
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

            string prefix = content.Length == 0 || DeploymentFileStore.EndsWithNewLine(content)
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
                || DeploymentFileStore.EndsWithNewLine(normalizedContent)
                    ? string.Empty
                    : newLine;
            return startMarker
                + newLine
                + normalizedContent
                + contentSeparator
                + endMarker;
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

        private sealed record TextInsertion(int CharacterIndex, string Text);

        private sealed record TextRange(int Start, int Length);

        private sealed record IndexedSegment(
            ScriptSegmentInfo Segment,
            int Index,
            string Trigger
        );
    }
}
