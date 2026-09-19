using System.IO;
using System.Text;
using Ude;

namespace Legend2Tool.WPF.Services
{
    public class EncodingService : IEncodingService
    {
        private const int minLengthForUDE = 100;
        private const double minConfidenceThreshold = 0.7;
        private HashSet<string> cjkCompetitors = new(StringComparer.OrdinalIgnoreCase)
        {
            "SHIFT_JIS",
            "X-EUC-JP",
            "EUC-KP",
            "BIG5",
            "ISO-2022-JP",
            "EUC-JP",
            "ISO-8859-1",
            "ISO-8859-2",
            "ISO-8859-5",
            "ISO-8859-15",
            "Windows-1252",
            "KOI8-R"
        };
        public void ConvertFileEncoding(string inputFilePath, string outputFilePath, Encoding inputFileEncoding, string targetEncodingName)
        {
            ConvertFileEncodingCore(inputFilePath, outputFilePath, inputFileEncoding, targetEncodingName, null);
        }

        public void ConvertFileEncoding(string inputFilePath, string outputFilePath, Encoding inputFileEncoding, string targetEncodingName, string backupFilePath)
        {
            ConvertFileEncodingCore(inputFilePath, outputFilePath, inputFileEncoding, targetEncodingName, backupFilePath);
        }

        private void ConvertFileEncodingCore(string inputFilePath, string outputFilePath, Encoding inputFileEncoding, string targetEncodingName, string? backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(inputFilePath))
            {
                throw new ArgumentException("输入文件路径不能为空", nameof(inputFilePath));
            }
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new ArgumentException("输出文件路径不能为空", nameof(outputFilePath));
            }
            if (string.IsNullOrWhiteSpace(targetEncodingName))
            {
                throw new ArgumentException("目标编码名称不能为空", nameof(targetEncodingName));
            }
            if (inputFileEncoding == null)
            {
                throw new ArgumentNullException(nameof(inputFileEncoding), "输入文件编码不能为空");
            }
            Encoding targetEncoding;
            try
            {
                if (targetEncodingName.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                {
                    targetEncoding = new UTF8Encoding(false); // 不带BOM的UTF-8
                }
                else
                {
                    targetEncoding = GetEncodingByName(targetEncodingName);
                }
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"无效的目标编码名称: {targetEncodingName}", nameof(targetEncodingName), ex);
            }

            try
            {
                if (backupFilePath is not null)
                {
                    string? backupDirectory = Path.GetDirectoryName(backupFilePath);
                    if (!string.IsNullOrEmpty(backupDirectory)) Directory.CreateDirectory(backupDirectory);
                    File.Copy(inputFilePath, backupFilePath, false);
                }

                Encoding strictSourceEncoding = CreateStrictEncoding(inputFileEncoding);
                Encoding strictTargetEncoding = CreateStrictEncoding(targetEncoding);
                string content;
                using (var reader = new StreamReader(inputFilePath, strictSourceEncoding, true))
                {
                    content = reader.ReadToEnd();
                }

                string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath))!;
                string tempFilePath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputFilePath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var writer = new StreamWriter(tempFilePath, false, strictTargetEncoding)) writer.Write(content);
                    File.Move(tempFilePath, outputFilePath, true);
                }
                finally
                {
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                }
            }
            catch (FileNotFoundException ex)
            {
                throw new FileNotFoundException($"输入文件未找到: {inputFilePath}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new DirectoryNotFoundException($"输出目录未找到: {Path.GetDirectoryName(outputFilePath)}", ex);
            }
            catch (IOException ex)
            {
                throw new IOException($"读取或写入文件错误: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"没有权限访问文件: {inputFilePath} or {outputFilePath}", ex);
            }
        }

        private static Encoding CreateStrictEncoding(Encoding encoding)
        {
            bool emitBom = encoding.GetPreamble().Length > 0;
            return encoding.CodePage switch
            {
                65001 => new UTF8Encoding(emitBom, true),
                1200 => new UnicodeEncoding(false, emitBom, true),
                1201 => new UnicodeEncoding(true, emitBom, true),
                12000 => new UTF32Encoding(false, emitBom, true),
                12001 => new UTF32Encoding(true, emitBom, true),
                _ => Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            };
        }

        public Encoding DetectBom(byte[] buffer)
        {
            if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                return new UTF8Encoding(true); // UTF-8带BOM
            }
            if (
                buffer.Length >= 4
                && buffer[0] == 0x00
                && buffer[1] == 0x00
                && buffer[2] == 0xFE
                && buffer[3] == 0xFF
            )
            {
                return new UTF32Encoding(true, true); // UTF-32 BE
            }
            if (
                buffer.Length >= 4
                && buffer[0] == 0xFF
                && buffer[1] == 0xFE
                && buffer[2] == 0x00
                && buffer[3] == 0x00
            )
            {
                return new UTF32Encoding(false, true); // UTF-32 LE
            }
            if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            {
                return Encoding.Unicode; // UTF-16 LE
            }
            if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode; // UTF-16 BE
            }
            return null!; // 未检测到BOM
        }

        public Encoding DetectFileEncoding(string filePath)
        {
            EncodingDetectionResult result = DetectFileEncodingResult(filePath);
            // ASCII and empty files have no byte-level encoding evidence. UTF-8 is a safe
            // compatibility value for readers because their contents decode identically.
            return result.Encoding ?? Encoding.UTF8;
        }

        public EncodingDetectionResult DetectFileEncodingResult(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件未找到：{filePath}");

            const int BytesToReadForDetection = 8192;
            byte[] buffer;
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int bytesToRead = (int)Math.Min(fs.Length, BytesToReadForDetection);
                    buffer = new byte[bytesToRead];
                    fs.Read(buffer, 0, bytesToRead);
                }
            }
            catch (IOException ex)
            {
                throw new IOException($"无法读取文件：{filePath}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"没有权限访问文件：{filePath}", ex);
            }

            // 检测BOM
            Encoding encoding = DetectBom(buffer);
            if (encoding != null)
                return new EncodingDetectionResult(encoding, "BOM");

            if (buffer.Length == 0)
                return new EncodingDetectionResult(null, "空文件没有可用于判断编码的内容");

            if (IsPureAscii(buffer))
                return new EncodingDetectionResult(null, "纯 ASCII 内容无法区分 UTF-8 与 GB18030");

            if (buffer.Length < minLengthForUDE)
            {
                return CanDecodeAsUtf8(buffer)
                    ? new EncodingDetectionResult(Encoding.UTF8, "严格 UTF-8 校验通过")
                    : new EncodingDetectionResult(Encoding.GetEncoding("GB18030"), "严格 UTF-8 校验失败");
            }

            // 使用UDE库检测
            var charsetDetector = new CharsetDetector();
            charsetDetector.Feed(buffer, 0, buffer.Length);
            charsetDetector.DataEnd();
            if (charsetDetector.Charset != null && charsetDetector.Confidence >= minConfidenceThreshold)
            {
                var detected = charsetDetector.Charset.ToUpperInvariant();
                if (cjkCompetitors.Contains(detected))
                {
                    return new EncodingDetectionResult(Encoding.GetEncoding("GB18030"), $"UDE: {detected}, {charsetDetector.Confidence:P0}");
                }

                var safeEncoding = GetSafeEncoding(detected);
                if (safeEncoding != null) return new EncodingDetectionResult(safeEncoding, $"UDE: {detected}, {charsetDetector.Confidence:P0}");
            }
            return new EncodingDetectionResult(null, "检测结果置信度不足");
        }

        private bool CanDecodeAsUtf8(byte[] buffer)
        {
            try
            {
                var utf8 = new UTF8Encoding(false, true); // ThrowOnInvalidBytes = true
                string text = utf8.GetString(buffer);     // 尝试解码
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private bool IsPureAscii(byte[] buffer)
        {
            foreach (byte b in buffer)
            {
                if (b > 127)
                {
                    return false;
                }
            }
            return true;
        }

        private Encoding? GetSafeEncoding(string detected)
        {
            try
            {
                return Encoding.GetEncoding(detected);
            }
            catch
            {
                return null;
            }
        }

        public Encoding GetEncodingByName(string encodingName)
        {
            if (string.IsNullOrWhiteSpace(encodingName))
            {
                throw new ArgumentException("编码名字不能为空", nameof(encodingName));
            }
            if (encodingName.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
            {
                return new UTF8Encoding(false); // 不带BOM的UTF-8
            }
            try
            {
                return Encoding.GetEncoding(encodingName);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"无法识别编码：{encodingName}", nameof(encodingName));
            }
        }
    }
}
