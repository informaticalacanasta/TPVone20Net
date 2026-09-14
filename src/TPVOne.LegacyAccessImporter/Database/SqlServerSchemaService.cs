using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Import;
using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccessImporter.Database;

internal sealed class SqlServerSchemaService : ILegacySqlSchemaPort
{
    private readonly string _connectionString;
    private readonly LegacySqlDdlBuilder _ddlBuilder;
    private readonly int _commandTimeout;

    public SqlServerSchemaService(
        string connectionString,
        DaoToSqlTypeMapper typeMapper,
        int commandTimeout)
    {
        _connectionString = connectionString;
        _ddlBuilder = new LegacySqlDdlBuilder(typeMapper);
        _commandTimeout = commandTimeout;
    }

    public async Task<bool> TableExistsAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN OBJECT_ID(
                N'dbo.' + QUOTENAME(@TableName), N'U') IS NULL
                THEN 0 ELSE 1 END;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _commandTimeout
        };
        command.Parameters.AddWithValue("@TableName", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task CreateTableAsync(
        LegacyTableSchema table,
        CancellationToken cancellationToken)
    {
        return CreateTableAsync(table, table.Name, cancellationToken);
    }

    public async Task CreateTableAsync(
        LegacyTableSchema table,
        string destinationTableName,
        CancellationToken cancellationToken)
    {
        var sql = _ddlBuilder.BuildCreateTableSql(table, destinationTableName);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction)
        {
            CommandTimeout = _commandTimeout
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public string BuildCreateTableSql(LegacyTableSchema table, string destinationTableName)
    {
        return _ddlBuilder.BuildCreateTableSql(table, destinationTableName);
    }

    public async Task<long> CountRowsAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await CountRowsAsync(connection, transaction: null, tableName, cancellationToken);
    }

    public async Task<SqlTableSchema> ReadTableSchemaAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        const string columnSql = """
            SELECT c.name, ty.name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, c.column_id
            FROM sys.columns c
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TableName), N'U')
            ORDER BY c.column_id;
            """;
        const string indexSql = """
            SELECT i.name, i.is_unique, i.is_primary_key, ic.key_ordinal,
                   c.name AS ColumnName, ic.is_descending_key
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TableName), N'U')
              AND i.is_hypothetical = 0
              AND ic.key_ordinal > 0
            ORDER BY i.index_id, ic.key_ordinal;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new List<SqlColumnSchema>();
        await using (var command = new SqlCommand(columnSql, connection)
        {
            CommandTimeout = _commandTimeout
        })
        {
            command.Parameters.AddWithValue("@TableName", tableName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var typeName = reader.GetString(1);
                var maxLength = reader.GetInt16(2);
                columns.Add(new(
                    reader.GetString(0),
                    new(
                        typeName,
                        typeName == "nvarchar" && maxLength > 0
                            ? maxLength / 2
                            : maxLength,
                        reader.GetByte(3),
                        reader.GetByte(4)),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetInt32(7) - 1));
            }
        }

        var indexRows = new List<IndexRow>();
        await using (var command = new SqlCommand(indexSql, connection)
        {
            CommandTimeout = _commandTimeout
        })
        {
            command.Parameters.AddWithValue("@TableName", tableName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexRows.Add(new(
                    reader.GetString(0),
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetByte(3),
                    reader.GetString(4),
                    reader.GetBoolean(5)));
            }
        }

        var indexes = indexRows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LegacyIndexSchema(
                group.Key,
                group.First().IsUnique,
                group.First().IsPrimary,
                group.Select(row => new LegacyIndexColumn(
                        row.ColumnName,
                        row.Ordinal,
                        row.IsDescending))
                    .OrderBy(column => column.Ordinal)
                    .ToArray()))
            .ToArray();

        return new(tableName, columns, indexes);
    }

    public async Task CreateIndexesAsync(
        LegacyTableSchema table,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await CreateIndexesAsync(
            connection,
            transaction: null,
            table,
            table.Name,
            cancellationToken);
    }

    public async Task CreateIndexesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        LegacyTableSchema table,
        string destinationTableName,
        CancellationToken cancellationToken)
    {
        var statements = _ddlBuilder.BuildCreateIndexSql(table, destinationTableName);
        for (var index = 0; index < table.Indexes.Count; index++)
        {
            var current = table.Indexes[index];
            if (await IndexExistsAsync(
                    connection,
                    transaction,
                    destinationTableName,
                    current.Name,
                    cancellationToken))
            {
                continue;
            }

            await using var command = transaction is null
                ? new SqlCommand(statements[index], connection)
                : new SqlCommand(statements[index], connection, transaction);
            command.CommandTimeout = _commandTimeout;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<long> CountRowsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql =
            $"SELECT COUNT_BIG(*) FROM dbo.{SqlIdentifier.Quote(tableName)};";
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = _commandTimeout;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> IndexExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        string indexName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TableName), N'U')
              AND name = @IndexName;
            """;
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = _commandTimeout;
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@IndexName", indexName);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private sealed record IndexRow(
        string Name,
        bool IsUnique,
        bool IsPrimary,
        int Ordinal,
        string ColumnName,
        bool IsDescending);
}
