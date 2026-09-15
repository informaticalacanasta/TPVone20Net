using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Planning;

public interface IReplacementOperations
{
    Task CreateEmptyTableAsync(
        string tableName,
        LegacyTableSchema schema,
        CancellationToken cancellationToken);

    Task<long> CopyDataAsync(string tableName, CancellationToken cancellationToken);

    Task CreateIndexesAsync(
        string tableName,
        LegacyTableSchema schema,
        CancellationToken cancellationToken);

    Task<long> CountAsync(string tableName, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string tableName, CancellationToken cancellationToken);

    Task SwapAtomicAsync(
        string destinationTableName,
        string stagingTableName,
        string? backupTableName,
        CancellationToken cancellationToken);

    Task DropIfExistsAsync(string tableName, CancellationToken cancellationToken);
}

public sealed class SafeReplacementWorkflow
{
    public async Task<long> ReplaceOrCreateAsync(
        IReplacementOperations operations,
        LegacyTableSchema schema,
        string destinationTableName,
        string stagingTableName,
        string backupTableName,
        CancellationToken cancellationToken = default)
    {
        var destinationExisted = await operations.ExistsAsync(
            destinationTableName,
            cancellationToken);

        try
        {
            await operations.CreateEmptyTableAsync(
                stagingTableName,
                schema,
                cancellationToken);
            var imported = await operations.CopyDataAsync(
                stagingTableName,
                cancellationToken);
            if (!RowCountValidator.Matches(schema.RowCount, imported))
            {
                throw new DataImportException(
                    $"La validación de filas falló para '{schema.Name}': " +
                    $"origen={schema.RowCount}, staging importado={imported}.");
            }

            await operations.CreateIndexesAsync(
                stagingTableName,
                schema,
                cancellationToken);

            var counted = await operations.CountAsync(
                stagingTableName,
                cancellationToken);
            if (!RowCountValidator.Matches(schema.RowCount, counted))
            {
                throw new DataImportException(
                    $"La validación de filas falló para '{schema.Name}': " +
                    $"origen={schema.RowCount}, staging={counted}.");
            }

            await operations.SwapAtomicAsync(
                destinationTableName,
                stagingTableName,
                destinationExisted ? backupTableName : null,
                cancellationToken);

            if (destinationExisted)
            {
                try
                {
                    await operations.DropIfExistsAsync(backupTableName, cancellationToken);
                }
                catch
                {
                    // El swap ya confirmó la tabla definitiva; un backup huérfano no revierte el éxito.
                }
            }

            return counted;
        }
        catch (Exception exception)
        {
            Exception? cleanup = null;
            try
            {
                await operations.DropIfExistsAsync(stagingTableName, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                cleanup = cleanupException;
            }

            if (cleanup is not null)
            {
                throw new DataImportException(
                    $"{exception.Message} Además no se pudo eliminar la tabla staging '{stagingTableName}'.",
                    new AggregateException(exception, cleanup));
            }

            throw;
        }
    }
}
