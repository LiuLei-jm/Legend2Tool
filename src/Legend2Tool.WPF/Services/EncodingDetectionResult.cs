using System.Text;

namespace Legend2Tool.WPF.Services;

public sealed record EncodingDetectionResult(Encoding? Encoding, string Reason)
{
    public bool IsKnown => Encoding is not null;
}
