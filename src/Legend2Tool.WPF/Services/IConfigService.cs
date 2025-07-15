using Legend2Tool.WPF.Enums;
using System.Text;

namespace Legend2Tool.WPF.Services
{
    public interface IConfigService
    {
        EngineType CheckEngineType(string serverDirectory);
        Task<string> GetExternalIpAddressAsync();
        T ReadSectionConfig<T>(string filePath, Encoding fileEncoding, string sectionName) where T : class, new();
        void WriteSectionConfig<T>(string filePath, T config, Encoding fileEncoding, string sectionName) where T : class, new();
        T ReadMultiSectionConfig<T>(string filePath, Encoding fileEncoding) where T : class, new();
        void WriteMultiSectionConfig<T>(string filePath, T config, Encoding fileEncoding) where T : class, new();
    }
}
