using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccessImporter.Conversion;

internal sealed class ConversionHistoryService
{
    private readonly string _connectionString;
    private readonly int _commandTimeout;

    public ConversionHistoryService(string connectionString, int commandTimeout)
    {
        _connectionString = connectionString;
        _commandTimeout = commandTimeout;
    }

    public async Task<string?> GetSuccessfulSourceHashAsync(
        string convertedFile,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) SourceHash
            FROM dbo.LegacyAccessConversionHistory
            WHERE ConvertedFile = @ConvertedFile
              AND Status IN (N'Success', N'Copied')
            ORDER BY Id DESC;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@ConvertedFile", convertedFile);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string hash ? hash : null;
    }

    public async Task<bool> WasSuccessfullyConvertedAsync(
        string sourceHash,
        string convertedFile,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.LegacyAccessConversionHistory
                WHERE SourceHash = @SourceHash
                  AND ConvertedFile = @ConvertedFile
                  AND Status IN (N'Success', N'Copied')
            ) THEN 1 ELSE 0 END;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@SourceHash", sourceHash);
        command.Parameters.AddWithValue("@ConvertedFile", convertedFile);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task RecordAsync(
        ConversionItemResult item,
        FileInfo source,
        FileInfo? converted,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.LegacyAccessConversionHistory
            (
                SourceFile, SourceFileName, SourceFileSize, SourceLastWriteTime,
                SourceHash, ConvertedFile, ConvertedFileSize, ConvertedHash,
                SourceFormat, Action, StartedAt, FinishedAt, Status, ErrorMessage
            )
            VALUES
            (
                @SourceFile, @SourceFileName, @SourceFileSize, @SourceLastWriteTime,
                @SourceHash, @ConvertedFile, @ConvertedFileSize, @ConvertedHash,
                @SourceFormat, @Action, SYSDATETIME(), SYSDATETIME(), @Status, @ErrorMessage
            );
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@SourceFile", item.OriginalFile);
        command.Parameters.AddWithValue("@SourceFileName", source.Name);
        command.Parameters.AddWithValue("@SourceFileSize", source.Length);
        command.Parameters.AddWithValue("@SourceLastWriteTime", source.LastWriteTimeUtc);
        command.Parameters.AddWithValue("@SourceHash", item.SourceHash);
        command.Parameters.AddWithValue(
            "@ConvertedFile",
            (object?)item.ConvertedFile ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ConvertedFileSize",
            converted is null ? DBNull.Value : converted.Length);
        command.Parameters.AddWithValue(
            "@ConvertedHash",
                converted is null
                ? DBNull.Value
                : await FileHashCalculator.CalculateSha256Async(
                    converted.FullName,
                    cancellationToken));
        command.Parameters.AddWithValue("@SourceFormat", item.Format.ToString());
        command.Parameters.AddWithValue("@Action", item.Action.ToString());
        command.Parameters.AddWithValue("@Status", item.Status);
        command.Parameters.AddWithValue(
            "@ErrorMessage",
            string.IsNullOrWhiteSpace(item.Message) || item.Status is "Success" or "Copied" or "Skipped"
                ? DBNull.Value
                : item.Message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
