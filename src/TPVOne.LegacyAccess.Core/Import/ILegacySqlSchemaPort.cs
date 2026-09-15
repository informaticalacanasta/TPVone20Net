using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Import;

public interface ILegacySqlSchemaPort
{
    Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken);

    Task DropTableAsync(string tableName, CancellationToken cancellationToken);

    Task CreateTableAsync(
        LegacyTableSchema table,
        string destinationTableName,
        CancellationToken cancellationToken);

    Task CreateTableAsync(LegacyTableSchema table, CancellationToken cancellationToken)
        => CreateTableAsync(table, table.Name, cancellationToken);

    Task CreateIndexesAsync(
        LegacyTableSchema table,
        string destinationTableName,
        string? uniqueNameSuffix,
        CancellationToken cancellationToken);

    Task CreateIndexesAsync(LegacyTableSchema table, CancellationToken cancellationToken)
        => CreateIndexesAsync(table, table.Name, uniqueNameSuffix: null, cancellationToken);

    Task SwapAtomicAsync(
        string destinationTableName,
        string stagingTableName,
        string? backupTableName,
        IReadOnlyList<LegacyIndexSchema> indexes,
        string? uniqueNameSuffix,
        CancellationToken cancellationToken);

    Task<SqlTableSchema> ReadTableSchemaAsync(
        string tableName,
        CancellationToken cancellationToken);

    Task<long> CountRowsAsync(string tableName, CancellationToken cancellationToken);
}
