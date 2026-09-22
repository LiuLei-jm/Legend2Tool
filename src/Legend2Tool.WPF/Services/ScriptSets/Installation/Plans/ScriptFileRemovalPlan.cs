using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;

internal sealed record ScriptFileRemovalPlan(
    string TargetPath,
    byte[]? OutputBytes
);
