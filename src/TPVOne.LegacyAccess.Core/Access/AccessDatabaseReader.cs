using System.Data;
using System.Data.OleDb;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccess.Core.Access;

public sealed class AccessDatabaseReader
{
    private readonly AccessProviderDetector _providerDetector;

    public AccessDatabaseReader(AccessProviderDetector providerDetector)
    {
        _providerDetector = providerDetector;
    }

    public async Task<AccessDatabaseSchema> ReadSchemaAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var hash = await FileHashCalculator.CalculateSha256Async(
            filePath,
            cancellationToken);
        var selection = _providerDetector.Detect(filePath, password);
        var warnings = new List<string>();

        try
        {
            using var connection = new OleDbConnection(selection.ConnectionString);
            connection.Open();

            var tables = ReadUserTableNames(connection)
                .Select(tableName => ReadTableSchema(connection, tableName, warnings))
                .OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            warnings.Add(
                "Las relaciones no se replican: OLE DB no las expone de forma " +
                "uniforme en todas las versiones Jet/ACE.");

            return new(
                filePath,
                hash,
                selection.Provider,
                tables,
                warnings);
        }
        catch (LegacyAccessException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SchemaExtractionException(
                $"No se pudo extraer el esquema de '{filePath}'.",
                exception);
        }
    }

    public OleDbConnection OpenConnection(
        string filePath,
        string provider,
        string? password = null)
    {
        var connection = new OleDbConnection(
            AccessProviderDetector.BuildConnectionString(provider, filePath, password));
        connection.Open();
        return connection;
    }

    public OleDbDataReader OpenTableReader(
        OleDbConnection connection,
        string tableName)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT * FROM {SqlIdentifier.Quote(tableName)}";
        return command.ExecuteReader(CommandBehavior.SequentialAccess)
            ?? throw new DataImportException(
                $"El proveedor no devolvió datos para la tabla '{tableName}'.");
    }

    private static IReadOnlyList<string> ReadUserTableNames(OleDbConnection connection)
    {
        var schema = connection.GetOleDbSchemaTable(
            OleDbSchemaGuid.Tables,
            [null, null, null, "TABLE"]);
        if (schema is null)
        {
            return [];
        }

        return schema.Rows
            .Cast<DataRow>()
            .Select(row => new
            {
                Name = Convert.ToString(row["TABLE_NAME"]) ?? string.Empty,
                Type = Convert.ToString(row["TABLE_TYPE"])
            })
            .Where(table => AccessObjectFilter.IsUserTable(table.Name, table.Type))
            .Select(table => table.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AccessTableSchema ReadTableSchema(
        OleDbConnection connection,
        string tableName,
        ICollection<string> warnings)
    {
        var defaults = ReadColumnDefaults(connection, tableName);
        using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText =
            $"SELECT * FROM {SqlIdentifier.Quote(tableName)} WHERE 1 = 0";
        using var reader = schemaCommand.ExecuteReader(CommandBehavior.SchemaOnly)
            ?? throw new SchemaExtractionException(
                $"No se pudo leer el esquema de la tabla '{tableName}'.");
        var schema = reader.GetSchemaTable()
            ?? throw new SchemaExtractionException(
                $"El proveedor no expuso columnas para '{tableName}'.");

        var columns = schema.Rows
            .Cast<DataRow>()
            .Select(row => ReadColumn(row, defaults))
            .OrderBy(column => column.Ordinal)
            .ToArray();

        var indexes = ReadIndexes(connection, tableName, warnings);

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText =
            $"SELECT COUNT(*) FROM {SqlIdentifier.Quote(tableName)}";
        var rowCount = Convert.ToInt64(countCommand.ExecuteScalar());

        return new(tableName, columns, indexes, rowCount);
    }

    private static AccessColumnSchema ReadColumn(
        DataRow row,
        IReadOnlyDictionary<string, string?> defaults)
    {
        var name = Convert.ToString(row["ColumnName"])
            ?? throw new SchemaExtractionException("Una columna no tiene nombre.");
        var providerType = (OleDbType)Convert.ToInt32(row["ProviderType"]);
        var dataType = row["DataType"] as Type;

        return new(
            name,
            providerType,
            dataType?.FullName ?? typeof(object).FullName!,
            GetNullableInt(row, "ColumnSize"),
            GetNullableByte(row, "NumericPrecision"),
            GetNullableByte(row, "NumericScale"),
            GetBoolean(row, "AllowDBNull", true),
            GetBoolean(row, "IsAutoIncrement", false),
            Convert.ToInt32(row["ColumnOrdinal"]),
            defaults.GetValueOrDefault(name));
    }

    private static IReadOnlyDictionary<string, string?> ReadColumnDefaults(
        OleDbConnection connection,
        string tableName)
    {
        try
        {
            var schema = connection.GetOleDbSchemaTable(
                OleDbSchemaGuid.Columns,
                [null, null, tableName, null]);
            if (schema is null || !schema.Columns.Contains("COLUMN_DEFAULT"))
            {
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            return schema.Rows
                .Cast<DataRow>()
                .Where(row => row["COLUMN_NAME"] is not DBNull)
                .GroupBy(
                    row => Convert.ToString(row["COLUMN_NAME"])!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First()["COLUMN_DEFAULT"] is DBNull
                        ? null
                        : Convert.ToString(group.First()["COLUMN_DEFAULT"]),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (OleDbException)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<AccessIndexSchema> ReadIndexes(
        OleDbConnection connection,
        string tableName,
        ICollection<string> warnings)
    {
        try
        {
            var schema = connection.GetOleDbSchemaTable(
                OleDbSchemaGuid.Indexes,
                [null, null, null, null, tableName]);
            if (schema is null)
            {
                return [];
            }

            return schema.Rows
                .Cast<DataRow>()
                .Where(row =>
                    row["INDEX_NAME"] is not DBNull &&
                    row["COLUMN_NAME"] is not DBNull)
                .GroupBy(
                    row => Convert.ToString(row["INDEX_NAME"])!,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new AccessIndexSchema(
                    group.Key,
                    GetBoolean(group.First(), "UNIQUE", false),
                    GetBoolean(group.First(), "PRIMARY_KEY", false),
                    group
                        .Select(row => new AccessIndexColumn(
                            Convert.ToString(row["COLUMN_NAME"])!,
                            GetNullableInt(row, "ORDINAL_POSITION") ?? 0,
                            GetNullableInt(row, "COLLATION") == 2))
                        .OrderBy(column => column.Ordinal)
                        .ToArray()))
                .ToArray();
        }
        catch (OleDbException exception)
        {
            warnings.Add(
                $"No se pudieron leer los índices de '{tableName}': {exception.Message}");
            return [];
        }
    }

    private static bool GetBoolean(
        DataRow row,
        string columnName,
        bool defaultValue)
    {
        return row.Table.Columns.Contains(columnName) &&
            row[columnName] is not DBNull
                ? Convert.ToBoolean(row[columnName])
                : defaultValue;
    }

    private static int? GetNullableInt(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(columnName) &&
            row[columnName] is not DBNull
                ? Convert.ToInt32(row[columnName])
                : null;
    }

    private static byte? GetNullableByte(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(columnName) &&
            row[columnName] is not DBNull
                ? Convert.ToByte(row[columnName])
                : null;
    }
}
