using System.IO;

namespace Legend2Tool.WPF.Services
{
    public interface IFileService
    {
        List<string> GetFiles(string directory, List<string> searchPatterns = null!, SearchOption searchOption = SearchOption.TopDirectoryOnly);
    }
}
