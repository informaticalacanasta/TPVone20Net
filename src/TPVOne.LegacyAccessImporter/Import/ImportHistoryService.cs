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
                  AND Status IN (N'Success', N'Replaced')
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

    public async Task<HashSet<(string Hash, string Table)>> LoadSuccessfulImportsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT SourceHash, TableName
            FROM dbo.LegacyImportHistory
            WHERE Status IN (N'Success', N'Replaced');
            """;
        var result = new HashSet<(string Hash, string Table)>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    public async Task<long> StartAsync(
        AccessDatabaseSchema database,
        AccessTableSchema table,
        ImportStatus status,
        long? previousRowCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.LegacyImportHistory
            (
                SourceFile, SourceFileName, SourceFileSize,
                SourceLastWriteTime, SourceHash, TableName,
                SourceRowCount, StartedAt, Status, Action, PreviousRowCount
            )
            OUTPUT INSERTED.Id
            VALUES
            (
                @SourceFile, @SourceFileName, @SourceFileSize,
                @SourceLastWriteTime, @SourceHash, @TableName,
                @SourceRowCount, SYSDATETIME(), @Status, @Action, @PreviousRowCount
            );
            """;
        var file = new FileInfo(database.OriginalFilePath ?? database.FilePath);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue(
            "@SourceFile",
            database.OriginalFilePath ?? database.FilePath);
        command.Parameters.AddWithValue("@SourceFileName", file.Name);
        command.Parameters.AddWithValue("@SourceFileSize", file.Exists ? file.Length : 0);
        command.Parameters.AddWithValue(
            "@SourceLastWriteTime",
            file.Exists ? file.LastWriteTimeUtc : DateTime.UtcNow);
        command.Parameters.AddWithValue("@SourceHash", database.SourceHash);
        command.Parameters.AddWithValue("@TableName", table.Name);
        command.Parameters.AddWithValue("@SourceRowCount", table.RowCount);
        command.Parameters.AddWithValue("@Status", status.ToString());
        command.Parameters.AddWithValue("@Action", status.ToString());
        command.Parameters.AddWithValue(
            "@PreviousRowCount",
            previousRowCount.HasValue ? previousRowCount.Value : DBNull.Value);
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
                Action = @Action,
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
        command.Parameters.AddWithValue("@Action", status.ToString());
        command.Parameters.AddWithValue(
            "@ErrorMessage",
            error is null ? DBNull.Value : error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
