using System.IO;

namespace Legend2Tool.WPF.Services
{
    public class FileService : IFileService
    {
        public List<string> GetFiles(string directory, List<string> searchPatterns = null!, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"路径不存在：'{directory}'.");
            }
            if (searchPatterns == null || !searchPatterns.Any())
            {
                searchPatterns = ["*.*"]; // Default to all files if no patterns are provided
            }
            var files = new List<string>();
            foreach (var pattern in searchPatterns)
            {
                files.AddRange(Directory.GetFiles(directory, pattern, searchOption));
            }
            return files;
        }
    }
}
