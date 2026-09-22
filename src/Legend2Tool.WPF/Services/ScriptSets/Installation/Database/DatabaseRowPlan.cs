using Legend2Tool.WPF.Models.ScriptSets;

namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database;

internal sealed record DatabaseRowPlan(
    GameDatabaseTableType TableType,
    string Name,
    Dictionary<string, object?> Values
);
