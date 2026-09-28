namespace Legend2Tool.WPF.Models.ScriptOptimizations;

public sealed record DropRateVersion(string Name, string Data)
{
    internal int Index { get; init; }
    internal string SourceText { get; init; } = string.Empty;
}
