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

    public async Task<long> ImportIntoAsync(
        AccessDatabaseSchema database,
        AccessTableSchema table,
        string destinationTableName,
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
                destinationTableName,
                cancellationToken);
            if (before != 0)
            {
                throw new DataImportException(
                    $"La tabla destino '{destinationTableName}' no está vacía. " +
                    "No se permite un SqlBulkCopy que duplique filas.");
            }

            var options = table.Columns.Any(column => column.IsAutoIncrement)
                ? SqlBulkCopyOptions.KeepIdentity
                : SqlBulkCopyOptions.Default;

            using var bulkCopy = new SqlBulkCopy(
                sqlConnection,
                options,
                transaction)
            {
                DestinationTableName =
                    $"dbo.{SqlIdentifier.Quote(destinationTableName)}",
                BatchSize = _options.BatchSize,
                BulkCopyTimeout = _options.CommandTimeoutSeconds,
                EnableStreaming = true
            };

            foreach (var column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.Name, column.Name);
            }

            await bulkCopy.WriteToServerAsync(accessDataReader, cancellationToken);

            var after = await _schemaService.CountRowsAsync(
                sqlConnection,
                transaction,
                destinationTableName,
                cancellationToken);
            if (after != table.RowCount)
            {
                throw new DataImportException(
                    $"La validación de filas falló para '{table.Name}': " +
                    $"origen={table.RowCount}, destino={after}.");
            }

            await transaction.CommitAsync(cancellationToken);
            return after;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw exception is DataImportException
                ? exception
                : new DataImportException(
                    $"SqlBulkCopy falló. Archivo: {database.FilePath}; " +
                    $"Tabla: {table.Name}; Mensaje: {exception.Message}",
                    exception);
        }
    }
}
