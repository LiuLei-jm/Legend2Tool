using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Plans;

internal sealed record MaterialFilePlan(
    MaterialFileInfo MaterialFile,
    string TargetPath,
    string StagedPath
);
