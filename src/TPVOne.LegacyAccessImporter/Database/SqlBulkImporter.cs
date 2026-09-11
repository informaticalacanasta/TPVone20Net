using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccessImporter.Database;

internal sealed class SqlBulkImporter
{
    private readonly string _connectionString;
    private readonly AccessDatabaseReader _accessReader;
    private readonly SqlServerSchemaService _schemaService;
    private readonly LegacyAccessImportOptions _options;

    public SqlBulkImporter(
        string connectionString,
        AccessDatabaseReader accessReader,
        SqlServerSchemaService schemaService,
        LegacyAccessImportOptions options)
    {
        _connectionString = connectionString;
        _accessReader = accessReader;
        _schemaService = schemaService;
        _options = options;
    }

    public async Task<long> ImportAsync(
        AccessDatabaseSchema database,
        AccessTableSchema table,
        CancellationToken cancellationToken)
    {
        using var accessConnection = _accessReader.OpenConnection(
            database.FilePath,
            database.Provider);
        using var accessDataReader = _accessReader.OpenTableReader(
            accessConnection,
            table.Name);

        await using var sqlConnection = new SqlConnection(_connectionString);
        await sqlConnection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await sqlConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            var before = await _schemaService.CountRowsAsync(
                sqlConnection,
                transaction,
                table.Name,
                cancellationToken);
            var options = table.Columns.Any(column => column.IsAutoIncrement)
                ? SqlBulkCopyOptions.KeepIdentity
                : SqlBulkCopyOptions.Default;

            using var bulkCopy = new SqlBulkCopy(
                sqlConnection,
                options,
                transaction)
            {
                DestinationTableName =
                    $"dbo.{SqlIdentifier.Quote(table.Name)}",
                BatchSize = _options.BatchSize,
                BulkCopyTimeout = _options.CommandTimeoutSeconds,
                EnableStreaming = true
            };

            foreach (var column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.Name, column.Name);
            }

            await bulkCopy.WriteToServerAsync(accessDataReader, cancellationToken);
            await _schemaService.CreateIndexesAsync(
                sqlConnection,
                transaction,
                table,
                cancellationToken);

            var after = await _schemaService.CountRowsAsync(
                sqlConnection,
                transaction,
                table.Name,
                cancellationToken);
            var imported = after - before;
            if (imported != table.RowCount)
            {
                throw new DataImportException(
                    $"La validación de filas falló para '{table.Name}': " +
                    $"origen={table.RowCount}, diferencia SQL={imported}.");
            }

            await transaction.CommitAsync(cancellationToken);
            return imported;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw exception is DataImportException
                ? exception
                : new DataImportException(
                    $"SqlBulkCopy falló para '{database.FilePath}', " +
                    $"tabla '{table.Name}'.",
                    exception);
        }
    }
}
