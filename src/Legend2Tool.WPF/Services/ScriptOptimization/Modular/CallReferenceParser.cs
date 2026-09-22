namespace Legend2Tool.WPF.Services.ScriptOptimization.Modular;

internal sealed class CallReferenceParser
{
    public (string Path, string Field) Parse(string line)
    {
        line = line.Trim();
        var startIndex = line.IndexOf('[');
        var endIndex = line.IndexOf(']');
        if (startIndex < 0 || endIndex <= startIndex)
        {
            return (string.Empty, string.Empty);
        }

        var path = line[(startIndex + 1)..endIndex];
        while (path.StartsWith('\\') || path.StartsWith('/'))
        {
            path = path[1..];
        }

        path = path.Replace('/', '\\');
        startIndex = line.IndexOf('@');
        return (path, startIndex >= 0 ? line[startIndex..] : string.Empty);
    }
}
