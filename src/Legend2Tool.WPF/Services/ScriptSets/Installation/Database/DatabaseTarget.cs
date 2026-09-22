using Legend2Tool.WPF.Enums;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database;

internal sealed record DatabaseTarget(
    string Path,
    DatabaseProvider Provider,
    EngineType EngineType
);
