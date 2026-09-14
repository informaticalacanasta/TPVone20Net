using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Import;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Planning;
using TPVOne.LegacyAccess.Core.Sources;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccessImporter.Database;

internal sealed class SqlBulkImporter : ILegacyDataCopyPort
{
    private readonly string _connectionString;
    private readonly SqlServerSchemaService _schemaService;
    private readonly LegacyImportOptions _options;

    public SqlBulkImporter(
        string connectionString,
        SqlServerSchemaService schemaService,
        LegacyImportOptions options)
    {
        _connectionString = connectionString;
        _schemaService = schemaService;
        _options = options;
    }

    public async Task<long> CopyAsync(
        LegacyTableSchema schema,
        ILegacyDataSource dataSource,
        string destinationTableName,
        long expectedRowCount,
        CancellationToken cancellationToken)
    {
        using var dataReader = dataSource.OpenReader(schema);

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

            var options = schema.Columns.Any(column => column.IsAutoIncrement)
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

            foreach (var column in schema.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.Name, column.Name);
            }

            await bulkCopy.WriteToServerAsync(dataReader, cancellationToken);

            var after = await _schemaService.CountRowsAsync(
                sqlConnection,
                transaction,
                destinationTableName,
                cancellationToken);
            if (!RowCountValidator.Matches(expectedRowCount, after))
            {
                throw new DataImportException(
                    $"La validación de filas falló para '{schema.Name}': " +
                    $"origen={expectedRowCount}, destino={after}.");
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
                    $"SqlBulkCopy falló. Origen: {dataSource.Location}; " +
                    $"Tabla: {schema.Name}; Mensaje: {exception.Message}",
                    exception);
        }
    }
}
