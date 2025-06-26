using System.Text;

namespace Legend2Tool.WPF.Services
{
    public interface IEncodingService
    {
        Encoding DetectFileEncoding(string filePath);
        Encoding DetectBom(byte[] buffer);
        void ConvertFileEncoding(
           string inputFilePath,
           string outputFilePath,
           Encoding inputFileEncoding,
           string targetEncodingName
       );
        Encoding GetEncodingByName(string encodingName);
    }
}
