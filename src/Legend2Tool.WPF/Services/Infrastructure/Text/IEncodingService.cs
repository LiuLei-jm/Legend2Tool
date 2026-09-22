using System.Text;

namespace Legend2Tool.WPF.Services.Infrastructure.Text
{
    public interface IEncodingService
    {
        Encoding DetectFileEncoding(string filePath);
        Encoding DetectFileEncoding(string filePath, Encoding fallbackEncoding);
        EncodingDetectionResult DetectFileEncodingResult(string filePath);
        Encoding DetectBom(byte[] buffer);
        void ConvertFileEncoding(
           string inputFilePath,
           string outputFilePath,
           Encoding inputFileEncoding,
           string targetEncodingName
       );
        void ConvertFileEncoding(
           string inputFilePath,
           string outputFilePath,
           Encoding inputFileEncoding,
           string targetEncodingName,
           string backupFilePath
       );
        Encoding GetEncodingByName(string encodingName);
    }
}
