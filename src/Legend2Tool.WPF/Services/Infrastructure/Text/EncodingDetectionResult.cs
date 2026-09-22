using System.Text;

namespace Legend2Tool.WPF.Services.Infrastructure.Text;

public sealed record EncodingDetectionResult(Encoding? Encoding, string Reason)
{
    public bool IsKnown => Encoding is not null;
}
