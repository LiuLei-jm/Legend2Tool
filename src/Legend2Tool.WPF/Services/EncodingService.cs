using System.IO;
using System.Text;
using Ude;

namespace Legend2Tool.WPF.Services
{
    public class EncodingService : IEncodingService
    {
        private const int minLengthForUDE = 100;
        private const double minConfidenceThreshold = 0.7;
        public void ConvertFileEncoding(string inputFilePath, string outputFilePath, Encoding inputFileEncoding, string targetEncodingName)
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
                string content;
                using (var reader = new StreamReader(inputFilePath, inputFileEncoding))
                {
                    content = reader.ReadToEnd();
                }
                using var writer = new StreamWriter(outputFilePath, false, targetEncoding);
                writer.Write(content);
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

        public Encoding DetectBom(byte[] buffer)
        {
            if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                return new UTF8Encoding(true); // UTF-8带BOM
            }
            if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            {
                return Encoding.Unicode; // UTF-16 LE
            }
            if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode; // UTF-16 BE
            }
            if (
                buffer.Length >= 4
                && buffer[0] == 0x00
                && buffer[1] == 0x00
                && buffer[2] == 0xFE
                && buffer[3] == 0xFF
            )
            {
                return Encoding.UTF32; // UTF-32 BE
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
            return null!; // 未检测到BOM
        }

        public Encoding DetectFileEncoding(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件未找到：{filePath}");

            long fileSize = new FileInfo(filePath).Length;
            if (fileSize <= 30)
                return Encoding.GetEncoding("GB18030");

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
                return encoding;

            if (fileSize < minLengthForUDE)
            {
                if (IsPureAscii(buffer))
                {
                    return Encoding.UTF8;
                }
                foreach (var fallbackEnc in GetCommonFallbackEncodings())
                {
                    try
                    {
                        fallbackEnc.GetString(buffer);
                        return fallbackEnc;
                    }
                    catch (DecoderFallbackException) { }
                    catch (ArgumentException) { }
                }
                return Encoding.GetEncoding("GB18030") ?? Encoding.UTF8;
            }

            // 使用UDE库检测
            var charsetDetector = new CharsetDetector();
            charsetDetector.Feed(buffer, 0, buffer.Length);
            charsetDetector.DataEnd();
            if (charsetDetector.Charset != null)
            {
                if (charsetDetector.Confidence >= minConfidenceThreshold)
                {
                    var detected = charsetDetector.Charset.ToUpperInvariant();
                    var safeEncoding = GetSafeEncoding(detected);
                    if (safeEncoding != null) return safeEncoding;
                }
                else
                {
                    foreach (var fallbackEnc in GetCommonFallbackEncodings())
                    {
                        try
                        {
                            fallbackEnc.GetString(buffer);
                            return fallbackEnc;
                        }
                        catch (DecoderFallbackException) { }
                        catch (ArgumentException) { }
                    }
                }
            }
            return Encoding.GetEncoding("GB18030") ?? Encoding.UTF8;
        }

        private IEnumerable<Encoding> GetCommonFallbackEncodings()
        {
            yield return Encoding.GetEncoding("GB18030");
            yield return Encoding.UTF8;
            yield return Encoding.Default;
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
