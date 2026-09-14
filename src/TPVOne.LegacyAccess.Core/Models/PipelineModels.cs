namespace TPVOne.LegacyAccess.Core.Models;

public enum ExistingTableAction
{
    Skip,
    Replace,
    Cancel
}

public enum PlannedTableStatus
{
    Create,
    SkipAlreadyImported,
    RequiresDecision,
    Collision,
    Failed
}

public enum ConversionAction
{
    SkipAlreadyConverted,
    CopyCompatible,
    ConvertToJet4,
    Reconvert
}

public enum AccessJetFormat
{
    Unknown,
    Jet3Access97,
    Jet4Access2000,
    AceAccdb
}

public sealed class LegacyAccessImportOptions
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string ConvertedDirectory { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 5000;
    public int CommandTimeoutSeconds { get; set; } = 120;
}

public enum ImportStatus
{
    Success,
    CompletedWithErrors,
    SkippedAlreadyImported,
    SkippedByUser,
    Replaced,
    Conflict,
    Failed,
    Cancelled
}

public sealed record TableImportResult(
    string SourceFile,
    string OriginalFile,
    string TableName,
    ImportStatus Status,
    bool TableCreated,
    long SourceRowCount,
    long ImportedRowCount,
    long? PreviousSqlRowCount,
    string? Error);

public sealed record PlannedTable(
    string OriginalFile,
    string ConvertedFile,
    string SourceHash,
    string TableName,
    long SourceRowCount,
    bool SqlExists,
    long? SqlRowCount,
    PlannedTableStatus Status,
    string Message);

public sealed record TableDecision(
    string SourceHash,
    string TableName,
    ExistingTableAction Action);

public sealed record ConversionItemResult(
    string OriginalFile,
    string? ConvertedFile,
    string SourceHash,
    AccessJetFormat Format,
    ConversionAction Action,
    string Status,
    string Message);

public sealed record ConversionBatchResult(
    int FilesFound,
    int Converted,
    int Copied,
    int Skipped,
    int Failed,
    IReadOnlyList<ConversionItemResult> Items,
    IReadOnlyList<string> Errors);

public sealed record PipelineResult(
    string Kind,
    ConversionBatchResult? Conversion,
    IReadOnlyList<AccessDatabaseSchema> Databases,
    IReadOnlyList<PlannedTable> Plan,
    IReadOnlyList<TableImportResult> Tables,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int FilesFound => Conversion?.FilesFound
        ?? Databases.Select(database => database.OriginalFilePath ?? database.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public int FilesOpened => Databases.Count;

    public int UserTables => Plan.Count > 0
        ? Plan.Count
        : Databases.Sum(database => database.Tables.Count);

    public long TotalSourceRows => Plan.Count > 0
        ? Plan.Sum(table => table.SourceRowCount)
        : Databases.Sum(database => database.Tables.Sum(table => table.RowCount));

    public bool IsSuccessful =>
        Errors.Count == 0 &&
        (Conversion?.Failed ?? 0) == 0 &&
        Tables.All(table => table.Status is
            ImportStatus.Success or
            ImportStatus.SkippedAlreadyImported or
            ImportStatus.SkippedByUser or
            ImportStatus.Replaced) &&
        Plan.All(table => table.Status != PlannedTableStatus.Failed);
}
