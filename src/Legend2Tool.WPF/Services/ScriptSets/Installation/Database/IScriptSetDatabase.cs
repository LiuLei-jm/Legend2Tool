namespace Legend2Tool.WPF.Services.ScriptSets.Installation.Database;

internal interface IScriptSetDatabase
{
    void Validate(DatabaseTarget target, IReadOnlyList<DatabaseRowPlan> plans);
    void Insert(DatabaseTarget target, IReadOnlyList<DatabaseRowPlan> plans, CancellationToken cancellationToken);
    int Remove(DatabaseTarget target, IReadOnlyList<DatabaseRowPlan> plans, CancellationToken cancellationToken);
}
