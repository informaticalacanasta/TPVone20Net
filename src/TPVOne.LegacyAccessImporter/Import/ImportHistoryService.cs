using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccessImporter.Import;

internal sealed class ImportHistoryService
{
    private readonly string _connectionString;
    private readonly int _commandTimeout;

    public ImportHistoryService(string connectionString, int commandTimeout)
    {
        _connectionString = connectionString;
        _commandTimeout = commandTimeout;
    }

    public static bool ShouldSkip(bool previousSuccessfulImport, bool forceImport)
    {
        return previousSuccessfulImport && !forceImport;
    }

    public async Task<bool> WasSuccessfullyImportedAsync(
        string sourceHash,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.LegacyImportHistory
                WHERE SourceHash = @SourceHash
                  AND TableName = @TableName
                  AND Status = N'Success'
            ) THEN 1 ELSE 0 END;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@SourceHash", sourceHash);
        command.Parameters.AddWithValue("@TableName", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<long> StartAsync(
        AccessDatabaseSchema database,
        AccessTableSchema table,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.LegacyImportHistory
            (
                SourceFile, SourceFileName, SourceFileSize,
                SourceLastWriteTime, SourceHash, TableName,
                SourceRowCount, StartedAt, Status
            )
            OUTPUT INSERTED.Id
            VALUES
            (
                @SourceFile, @SourceFileName, @SourceFileSize,
                @SourceLastWriteTime, @SourceHash, @TableName,
                @SourceRowCount, SYSDATETIME(), N'Started'
            );
            """;
        var file = new FileInfo(database.FilePath);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@SourceFile", database.FilePath);
        command.Parameters.AddWithValue("@SourceFileName", file.Name);
        command.Parameters.AddWithValue("@SourceFileSize", file.Length);
        command.Parameters.AddWithValue("@SourceLastWriteTime", file.LastWriteTimeUtc);
        command.Parameters.AddWithValue("@SourceHash", database.SourceHash);
        command.Parameters.AddWithValue("@TableName", table.Name);
        command.Parameters.AddWithValue("@SourceRowCount", table.RowCount);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task FinishAsync(
        long id,
        ImportStatus status,
        long importedRows,
        string? error,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.LegacyImportHistory
            SET ImportedRowCount = @ImportedRowCount,
                FinishedAt = SYSDATETIME(),
                Status = @Status,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@ImportedRowCount", importedRows);
        command.Parameters.AddWithValue("@Status", status.ToString());
        command.Parameters.AddWithValue(
            "@ErrorMessage",
            error is null ? DBNull.Value : error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
