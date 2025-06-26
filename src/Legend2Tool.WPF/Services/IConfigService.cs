using Legend2Tool.WPF.Enums;
using System.Text;

namespace Legend2Tool.WPF.Services
{
    public interface IConfigService
    {
        EngineType CheckEngineType(string serverDirectory);
        T ReadMultiSectionConfig<T>(string filePath, Encoding fileEncoding) where T : class, new();
    }
}
