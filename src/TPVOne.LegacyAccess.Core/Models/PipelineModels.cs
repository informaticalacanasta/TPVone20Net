namespace TPVOne.LegacyAccess.Core.Models;

public enum SchemaStatus
{
    Created,
    Replaced,
    AlreadyExists,
    Compatible,
    Conflict,
    Failed,
    NotProcessed
}

public enum DataStatus
{
    Imported,
    AlreadyImported,
    NotAvailable,
    Failed,
    NotProcessed
}

public enum PlannedTableStatus
{
    Create,
    SkipAlreadyImported,
    Compatible,
    Collision,
    Conflict,
    Failed
}

public enum ImportStatus
{
    Success,
    CompletedWithErrors,
    SkippedAlreadyImported,
    SkippedByUser,
    Conflict,
    Failed
}

public sealed class LegacyImportOptions
{
    public string SourceDirectory { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 5000;
    public int CommandTimeoutSeconds { get; set; } = 120;
    public bool ForceImport { get; set; }
    public bool OverwriteAll { get; set; }
    public string DefaultEncoding { get; set; } = "windows-1252";
    public string Delimiter { get; set; } = "|";
}

public sealed record TableImportResult(
    string TableName,
    string SchemaLocation,
    string? DataLocation,
    string StructureHash,
    string? DataHash,
    SchemaStatus SchemaStatus,
    DataStatus DataStatus,
    ImportStatus Status,
    bool TableCreated,
    long SourceRowCount,
    long ImportedRowCount,
    long? PreviousSqlRowCount,
    string? Error);

public sealed record PlannedTable(
    string SchemaFile,
    string? DataFile,
    string StructureHash,
    string? DataHash,
    string TableName,
    long SourceRowCount,
    bool SqlExists,
    long? SqlRowCount,
    PlannedTableStatus Status,
    string Message);

public sealed record LegacyAnalysisItem(
    string LogicalName,
    string SchemaLocation,
    string? DataLocation,
    string StructureHash,
    string? DataHash,
    int ColumnCount,
    int IndexCount,
    long AvailableRecords,
    bool HasDataSource,
    IReadOnlyList<string> Warnings,
    string? Error,
    string? DataError = null);

public sealed record PipelineResult(
    string Kind,
    IReadOnlyList<LegacyAnalysisItem> Analysis,
    IReadOnlyList<PlannedTable> Plan,
    IReadOnlyList<TableImportResult> Tables,
    IReadOnlyList<string> OrphanDataFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int SchemaDefinitionsFound => Analysis.Count;

    public int ValidLegacyTables =>
        Analysis.Count(item => item.Error is null);

    public int TablesWithoutData =>
        Analysis.Count(item => item.Error is null && !item.HasDataSource);

    public int AssociatedDataSources =>
        Analysis.Count(item => item.Error is null && item.HasDataSource);

    public int OrphanDataSources => OrphanDataFiles.Count;

    public int Columns =>
        Analysis.Where(item => item.Error is null).Sum(item => item.ColumnCount);

    public long AvailableRecords =>
        Analysis.Where(item => item.Error is null).Sum(item => item.AvailableRecords);

    public bool IsSuccessful =>
        Errors.Count == 0 &&
        OrphanDataFiles.Count == 0 &&
        Analysis.All(item => item.Error is null && item.DataError is null) &&
        Tables.All(table => table.Status is
            ImportStatus.Success or
            ImportStatus.SkippedAlreadyImported or
            ImportStatus.SkippedByUser) &&
        Plan.All(table => table.Status is not (
            PlannedTableStatus.Failed or
            PlannedTableStatus.Conflict or
            PlannedTableStatus.Collision));
}
