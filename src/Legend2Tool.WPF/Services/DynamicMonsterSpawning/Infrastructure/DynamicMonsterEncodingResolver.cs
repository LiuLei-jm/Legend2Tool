using Legend2Tool.WPF.Services.Infrastructure.Text;
using System.Text;

namespace Legend2Tool.WPF.Services.DynamicMonsterSpawning.Infrastructure;

internal sealed class DynamicMonsterEncodingResolver
{
    public Encoding Resolve(EncodingDetectionResult detection, Encoding fallback) =>
        detection.Encoding ?? fallback;
}
