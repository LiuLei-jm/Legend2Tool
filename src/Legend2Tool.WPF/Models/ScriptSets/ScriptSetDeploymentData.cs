namespace Legend2Tool.WPF.Models.ScriptSets
{
    public enum ScriptFileType
    {
        Whole = 1,
        Partial = 2
    }

    public enum GameDatabaseTableType
    {
        StdItems = 1,
        Monster = 2,
        Magic = 3
    }

    public sealed record ScriptSegmentInfo(
        Guid? Id,
        string TriggerField,
        string Content
    );

    public sealed record ScriptFileInfo(
        Guid Id,
        string FileName,
        string FilePath,
        ScriptFileType Type,
        string? WholeContent,
        List<ScriptSegmentInfo>? Segments
    );

    public sealed record ScriptSetDatabaseDataInfo(
        Guid Id,
        Guid ScriptSetId,
        GameDatabaseTableType TableType,
        string Name,
        string DataJson
    );

    public sealed record ScriptSetDeploymentData(
        IReadOnlyList<ScriptFileInfo> ScriptFiles,
        IReadOnlyList<ScriptSetDatabaseDataInfo> DatabaseRows
    );

    public sealed record ScriptSetInstallationResult(
        int ScriptFileCount,
        int DatabaseRowCount
    );

    public sealed record ScriptSetRemovalResult(
        int ScriptFileCount,
        int DatabaseRowCount
    );
}
