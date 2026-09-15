using System.Data.Common;
using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Data;
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

    public async Task<LegacyCopyResult> CopyAsync(
        LegacyTableSchema schema,
        ILegacyDataSource dataSource,
        string destinationTableName,
        long expectedRowCount,
        IProgress<LegacyCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var dataReader = OpenDbReader(dataSource, schema);

        await using var sqlConnection = new SqlConnection(_connectionString);
        await sqlConnection.OpenAsync(cancellationToken);

        var before = await _schemaService.CountRowsAsync(
            sqlConnection,
            transaction: null,
            destinationTableName,
            cancellationToken);
        if (before != 0)
        {
            throw new DataImportException(
                $"La tabla destino '{destinationTableName}' no está vacía. " +
                "No se permite un SqlBulkCopy que duplique filas.");
        }

        var bulkOptions = SqlBulkCopyOptions.TableLock;
        if (schema.Columns.Any(column => column.IsAutoIncrement))
        {
            bulkOptions |= SqlBulkCopyOptions.KeepIdentity;
        }

        long total = 0;
        var batches = 0;
        var exhausted = false;
        try
        {
            while (!exhausted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bounded = new BoundedDbDataReader(dataReader, _options.BatchSize);
                await using var transaction =
                    (SqlTransaction)await sqlConnection.BeginTransactionAsync(cancellationToken);
                try
                {
                    using var bulkCopy = new SqlBulkCopy(
                        sqlConnection,
                        bulkOptions,
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

                    await bulkCopy.WriteToServerAsync(bounded, cancellationToken);

                    if (bounded.RowsReturned == 0)
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        break;
                    }

                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Conserva el error original del lote.
                    }

                    throw;
                }

                batches++;
                total += bounded.RowsReturned;
                exhausted = bounded.SourceExhausted;
                progress?.Report(new LegacyCopyProgress(total, expectedRowCount, batches));
            }
        }
        catch (Exception exception) when (
            exception is not DataImportException and not OperationCanceledException)
        {
            throw new DataImportException(
                $"Error importando datos en staging de '{schema.Name}'.",
                exception);
        }

        if (!RowCountValidator.Matches(expectedRowCount, total))
        {
            throw new DataImportException(
                $"La validación de filas falló para '{schema.Name}': " +
                $"origen={expectedRowCount}, destino={total}.");
        }

        return new LegacyCopyResult(total, batches);
    }

    private static DbDataReader OpenDbReader(ILegacyDataSource dataSource, LegacyTableSchema schema)
    {
        var reader = dataSource.OpenReader(schema);
        return reader as DbDataReader
            ?? throw new DataImportException(
                $"El origen '{dataSource.Location}' no expone un DbDataReader.");
    }
}
