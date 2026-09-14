using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Planning;

namespace TPVOne.LegacyAccessImporter.Database;

internal sealed class SqlReplacementOperations : IReplacementOperations
{
    private readonly string _connectionString;
    private readonly SqlServerSchemaService _schemaService;
    private readonly SqlBulkImporter _bulkImporter;
    private readonly AccessDatabaseSchema _database;
    private readonly AccessTableSchema _table;
    private readonly int _commandTimeout;

    public SqlReplacementOperations(
        string connectionString,
        SqlServerSchemaService schemaService,
        SqlBulkImporter bulkImporter,
        AccessDatabaseSchema database,
        AccessTableSchema table,
        int commandTimeout)
    {
        _connectionString = connectionString;
        _schemaService = schemaService;
        _bulkImporter = bulkImporter;
        _database = database;
        _table = table;
        _commandTimeout = commandTimeout;
    }

    public Task CreateEmptyTableAsync(
        string tableName,
        AccessTableSchema schema,
        CancellationToken cancellationToken)
    {
        return _schemaService.CreateTableAsync(schema, tableName, cancellationToken);
    }

    public Task<long> CopyDataAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        return _bulkImporter.ImportIntoAsync(
            _database,
            _table,
            tableName,
            cancellationToken);
    }

    public async Task CreateIndexesAsync(
        string tableName,
        AccessTableSchema schema,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await _schemaService.CreateIndexesAsync(
            connection,
            transaction: null,
            schema,
            tableName,
            cancellationToken);
    }

    public Task<long> CountAsync(string tableName, CancellationToken cancellationToken)
    {
        return _schemaService.CountRowsAsync(tableName, cancellationToken);
    }

    public Task<bool> ExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        return _schemaService.TableExistsAsync(tableName, cancellationToken);
    }

    public async Task SwapAtomicAsync(
        string destinationTableName,
        string stagingTableName,
        string? backupTableName,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (backupTableName is not null)
            {
                await RenameAsync(
                    connection,
                    transaction,
                    destinationTableName,
                    backupTableName,
                    cancellationToken);
            }

            await RenameAsync(
                connection,
                transaction,
                stagingTableName,
                destinationTableName,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task DropIfExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        return _schemaService.DropTableIfExistsAsync(tableName, cancellationToken);
    }

    private async Task RenameAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string fromName,
        string toName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "EXEC sp_rename @From, @To;",
            connection,
            transaction)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue(
            "@From",
            $"dbo.{fromName}");
        command.Parameters.AddWithValue("@To", toName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
