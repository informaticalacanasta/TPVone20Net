namespace TPVOne.LegacyAccess.Core.Models;

public sealed class LegacyAccessImportOptions
{
    public string SourceDirectory { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 5000;
    public int CommandTimeoutSeconds { get; set; } = 120;
    public bool ForceImport { get; set; }
}

public enum ImportStatus
{
    Success,
    CompletedWithErrors,
    SkippedAlreadyImported,
    Conflict,
    Failed
}

public sealed record TableImportResult(
    string SourceFile,
    string TableName,
    ImportStatus Status,
    bool TableCreated,
    long SourceRowCount,
    long ImportedRowCount,
    string? Error);

public sealed record LegacyOperationResult(
    bool IsAnalysis,
    int FilesFound,
    int FilesOpened,
    int FilesWithErrors,
    int UserTables,
    int TotalColumns,
    long TotalSourceRows,
    IReadOnlyList<AccessDatabaseSchema> Databases,
    IReadOnlyList<TableImportResult> Tables,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccessful => Errors.Count == 0 &&
        Tables.All(table => table.Status is
            ImportStatus.Success or ImportStatus.SkippedAlreadyImported);
}

public sealed record ProviderAttempt(
    string Provider,
    string Status,
    string? Detail);

public sealed record AccessProviderSelection(
    string Provider,
    string ConnectionString,
    IReadOnlyList<ProviderAttempt> Attempts);
