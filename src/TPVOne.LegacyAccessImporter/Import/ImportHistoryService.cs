using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Import;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccessImporter.Import;

internal sealed class ImportHistoryService : ILegacyImportHistoryPort
{
    private readonly string _connectionString;
    private readonly int _commandTimeout;

    public ImportHistoryService(string connectionString, int commandTimeout)
    {
        _connectionString = connectionString;
        _commandTimeout = commandTimeout;
    }

    public async Task<bool> WasDataImportedAsync(
        string tableName,
        string dataHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.LegacyImportHistory
                WHERE TableName = @TableName
                  AND DataHash = @DataHash
                  AND DataStatus = N'Imported'
                  AND Status IN (N'Success', N'SkippedAlreadyImported')
            ) THEN 1 ELSE 0 END;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@DataHash", dataHash);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<long> RecordStartAsync(
        TableImportResult draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.LegacyImportHistory
            (
                SourceFile, SourceFileName, SourceFileSize,
                SourceLastWriteTime, SourceHash, TableName,
                SourceRowCount, StartedAt, Status, Action, PreviousRowCount,
                StructureFile, StructureHash, DataFile, DataHash,
                SchemaStatus, DataStatus
            )
            OUTPUT INSERTED.Id
            VALUES
            (
                @SourceFile, @SourceFileName, @SourceFileSize,
                @SourceLastWriteTime, @SourceHash, @TableName,
                @SourceRowCount, SYSDATETIME(), @Status, @Action, @PreviousRowCount,
                @StructureFile, @StructureHash, @DataFile, @DataHash,
                @SchemaStatus, @DataStatus
            );
            """;
        var file = new FileInfo(draft.SchemaLocation);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@SourceFile", draft.SchemaLocation);
        command.Parameters.AddWithValue("@SourceFileName", file.Name);
        command.Parameters.AddWithValue("@SourceFileSize", file.Exists ? file.Length : 0);
        command.Parameters.AddWithValue(
            "@SourceLastWriteTime",
            file.Exists ? file.LastWriteTimeUtc : DateTime.UtcNow);
        command.Parameters.AddWithValue("@SourceHash", draft.StructureHash);
        command.Parameters.AddWithValue("@TableName", draft.TableName);
        command.Parameters.AddWithValue("@SourceRowCount", draft.SourceRowCount);
        command.Parameters.AddWithValue("@Status", draft.Status.ToString());
        command.Parameters.AddWithValue("@Action", draft.Status.ToString());
        command.Parameters.AddWithValue(
            "@PreviousRowCount",
            draft.PreviousSqlRowCount.HasValue ? draft.PreviousSqlRowCount.Value : DBNull.Value);
        command.Parameters.AddWithValue("@StructureFile", draft.SchemaLocation);
        command.Parameters.AddWithValue("@StructureHash", draft.StructureHash);
        command.Parameters.AddWithValue(
            "@DataFile",
            (object?)draft.DataLocation ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@DataHash",
            (object?)draft.DataHash ?? DBNull.Value);
        command.Parameters.AddWithValue("@SchemaStatus", draft.SchemaStatus.ToString());
        command.Parameters.AddWithValue("@DataStatus", draft.DataStatus.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task RecordFinishAsync(
        long id,
        TableImportResult result,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.LegacyImportHistory
            SET ImportedRowCount = @ImportedRowCount,
                FinishedAt = SYSDATETIME(),
                Status = @Status,
                Action = @Action,
                ErrorMessage = @ErrorMessage,
                SchemaStatus = @SchemaStatus,
                DataStatus = @DataStatus,
                DataFile = @DataFile,
                DataHash = @DataHash
            WHERE Id = @Id;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@ImportedRowCount", result.ImportedRowCount);
        command.Parameters.AddWithValue("@Status", result.Status.ToString());
        command.Parameters.AddWithValue("@Action", result.Status.ToString());
        command.Parameters.AddWithValue(
            "@ErrorMessage",
            result.Error is null ? DBNull.Value : result.Error);
        command.Parameters.AddWithValue("@SchemaStatus", result.SchemaStatus.ToString());
        command.Parameters.AddWithValue("@DataStatus", result.DataStatus.ToString());
        command.Parameters.AddWithValue(
            "@DataFile",
            (object?)result.DataLocation ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@DataHash",
            (object?)result.DataHash ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
