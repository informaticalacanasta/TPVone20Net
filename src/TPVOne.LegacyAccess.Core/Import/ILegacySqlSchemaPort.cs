using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Import;

public interface ILegacySqlSchemaPort
{
    Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken);

    Task DropTableAsync(string tableName, CancellationToken cancellationToken);

    Task CreateTableAsync(LegacyTableSchema table, CancellationToken cancellationToken);

    Task CreateIndexesAsync(LegacyTableSchema table, CancellationToken cancellationToken);

    Task<SqlTableSchema> ReadTableSchemaAsync(
        string tableName,
        CancellationToken cancellationToken);

    Task<long> CountRowsAsync(string tableName, CancellationToken cancellationToken);
}
