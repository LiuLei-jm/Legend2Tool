using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;

internal sealed record ScriptFilePlan(
    Guid ScriptSetId,
    string TargetPath,
    ScriptFileInfo ScriptFile
);
