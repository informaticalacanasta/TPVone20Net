using System.Text;
using Microsoft.Data.SqlClient;
using TPVOne.LegacyAccess.Core.Import;
using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Sources;
using TPVOne.LegacyAccessImporter.Database;
using TPVOne.LegacyAccessImporter.Import;

namespace TPVOne.Tests;

public sealed class IsolatedSqlTests
{
    private static readonly string[] ForbiddenLegacyTables =
    [
        "HORAS",
        "pedidos_tmp",
        "pedidos_tmpp",
        "tiquetssii"
    ];

    private static readonly IReadOnlyList<string> TechnicalTables =
        ProtectedInfrastructureTables.Names;

    [Fact]
    public void InitialSchema_DoesNotPrecreateLegacyTables()
    {
        var sql = File.ReadAllText(MigrationPath("001_InitialSchema.sql"));

        Assert.DoesNotContain("CREATE TABLE dbo.HORAS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE dbo.pedidos_tmp", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE dbo.pedidos_tmpp", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE dbo.tiquetssii", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo.alergenos", sql, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task NewDatabase_CreatesOnlyTechnicalInfrastructure()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        var tables = await database.ListUserTablesAsync();

        Assert.NotEqual("TPVONE", database.DatabaseName);
        Assert.StartsWith("TPVONE_TESTS_", database.DatabaseName, StringComparison.Ordinal);
        foreach (var forbidden in ForbiddenLegacyTables)
        {
            Assert.DoesNotContain(forbidden, tables, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var technical in TechnicalTables)
        {
            Assert.Contains(technical, tables, StringComparer.OrdinalIgnoreCase);
        }
    }

    [SqlFact]
    public async Task ImportingAlergenosOnly_CreatesOnlyThatLegacyTable()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        using var source = AlergenosFixture.CreateSchemaOnly();
        var result = await ImportAsync(database, source.Path);

        var tables = await database.ListUserTablesAsync();
        var legacyTables = tables
            .Except(TechnicalTables, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(result.IsSuccessful);
        Assert.Equal(["alergenos"], legacyTables, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SchemaStatus.Created, result.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.NotAvailable, result.Tables[0].DataStatus);
        Assert.Equal(0, result.Tables[0].ImportedRowCount);

        var foto = await database.ReadColumnTypeAsync("alergenos", "FOTO_activado");
        Assert.Equal("varbinary", foto.TypeName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(-1, foto.MaxLength);
    }

    [SqlFact]
    public async Task DbMemo_IsImportedAsNvarcharText()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        const string expected = "Texto de prueba con jamón y paté";
        using var source = DbMemoFixture.Create(expected);

        var result = await ImportAsync(database, source.Path);
        var column = await database.ReadColumnTypeAsync("receta", "COMPOSICIO");
        var stored = await database.ReadNVarCharAsync("receta", "COMPOSICIO", "id", 1);

        Assert.True(result.IsSuccessful);
        Assert.Equal(SchemaStatus.Created, result.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.Imported, result.Tables[0].DataStatus);
        Assert.Equal("nvarchar", column.TypeName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(-1, column.MaxLength);
        Assert.Equal(expected, stored);
    }

    [SqlFact]
    public async Task LongBinaryUtf16Text_IsImportedAsNvarchar_WhileFotoStaysVarbinary()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        const string expectedText = "PAN DE CENTENO ALEMAN DE GRANO GRUESO";
        var expectedFoto = BinaryTextFixture.MinimalBmp();
        using var source = BinaryTextFixture.Create(expectedText, expectedFoto);

        var result = await ImportAsync(database, source.Path);
        var composicio = await database.ReadColumnTypeAsync("articulos", "COMPOSICIO");
        var foto = await database.ReadColumnTypeAsync("articulos", "FOTO");
        var storedText = await database.ReadNVarCharAsync("articulos", "COMPOSICIO", "id", 1);
        var storedFoto = await database.ReadBinaryAsync("articulos", "FOTO", "id", 1);

        Assert.True(result.IsSuccessful);
        Assert.Equal("nvarchar", composicio.TypeName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(-1, composicio.MaxLength);
        Assert.Equal(expectedText, storedText);
        Assert.Equal("varbinary", foto.TypeName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(-1, foto.MaxLength);
        Assert.Equal(expectedFoto, storedFoto);
    }

    [SqlFact]
    public async Task SchemaOnlySecondRun_DoesNotDropExistingTable()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        using var source = AlergenosFixture.CreateSchemaOnly();
        await ImportAsync(database, source.Path);

        var second = await ImportAsync(database, source.Path);
        var tables = await database.ListUserTablesAsync();

        Assert.Equal(SchemaStatus.AlreadyExists, second.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.NotAvailable, second.Tables[0].DataStatus);
        Assert.Contains("alergenos", tables, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SchemaMigrations", tables, StringComparer.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task InvalidCsvHeader_DoesNotDropExistingTable()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        using var source = AlergenosFixture.CreateWithCsv();
        await ImportAsync(database, source.Path);
        File.WriteAllText(
            Path.Combine(source.Path, "alergenos.csv"),
            "id_alergeno|OTRO|\n1|x|\n");

        var failed = await ImportAsync(database, source.Path);
        var rows = await database.CountRowsAsync("alergenos");
        var tables = await database.ListUserTablesAsync();

        Assert.Equal(DataStatus.Failed, failed.Tables[0].DataStatus);
        Assert.Equal(2, rows);
        Assert.Contains("alergenos", tables, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("cabecera", failed.Tables[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task IdenticalSecondImport_DeclineKeepsExistingRows()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        using var source = AlergenosFixture.CreateWithCsv();

        var first = await ImportAsync(database, source.Path);
        var second = await ImportAsync(database, source.Path, ScriptedOverwrite.With(false));
        var rows = await database.CountRowsAsync("alergenos");
        var history = await database.ReadHistoryStatusesAsync("alergenos");

        Assert.Equal(ImportStatus.Success, first.Tables[0].Status);
        Assert.Equal(DataStatus.Imported, first.Tables[0].DataStatus);
        Assert.Equal(2, first.Tables[0].ImportedRowCount);
        Assert.Equal(ImportStatus.SkippedByUser, second.Tables[0].Status);
        Assert.Equal(DataStatus.NotProcessed, second.Tables[0].DataStatus);
        Assert.Equal(0, second.Tables[0].ImportedRowCount);
        Assert.Equal(2, rows);
        Assert.Equal(
            ["Success", "SkippedByUser"],
            history);
    }

    [SqlFact]
    public async Task ExistingRows_OverwriteDropsAndReimports()
    {
        await using var database = await IsolatedSqlDatabase.CreateAsync();
        using var source = AlergenosFixture.CreateWithCsv();
        await ImportAsync(database, source.Path);
        await database.ExecuteAsync(
            "INSERT INTO dbo.alergenos (id_alergeno, DESCRIPCIO) VALUES (99, N'EXTRA');");

        var overwritten = await ImportAsync(database, source.Path, ScriptedOverwrite.With(true));
        var rows = await database.CountRowsAsync("alergenos");
        var history = await database.ReadHistoryStatusesAsync("alergenos");

        Assert.Equal(SchemaStatus.Replaced, overwritten.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.Imported, overwritten.Tables[0].DataStatus);
        Assert.Equal(2, overwritten.Tables[0].ImportedRowCount);
        Assert.Equal(2, rows);
        Assert.Equal(
            ["Success", "Success"],
            history);
    }

    private static async Task<PipelineResult> ImportAsync(
        IsolatedSqlDatabase database,
        string sourceDirectory,
        ILegacyImportInteraction? interaction = null)
    {
        var options = new LegacyImportOptions
        {
            SourceDirectory = sourceDirectory
        };
        var mapper = new DaoToSqlTypeMapper();
        var schema = new SqlServerSchemaService(database.ConnectionString, mapper, 30);
        var bulk = new SqlBulkImporter(database.ConnectionString, schema, options);
        var history = new ImportHistoryService(database.ConnectionString, 30);
        var coordinator = new LegacyImportCoordinator(
            new FileLegacyTableSourceScanner(options),
            mapper,
            new SchemaComparisonService(mapper),
            schema,
            bulk,
            history,
            options,
            interaction ?? ScriptedOverwrite.Unexpected());
        return await coordinator.ImportAsync();
    }

    private static string MigrationPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Migrations", fileName);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (!IsolatedSqlDatabase.IsAvailable)
        {
            Skip = "SQL Server no está disponible en localhost.";
        }
    }
}

file static class BinaryTextFixture
{
    public static TempDir Create(string composicio, byte[] foto)
    {
        var directory = new TempDir();
        File.WriteAllText(
            Path.Combine(directory.Path, "articulos.txt"),
            """
            Nombre Tabla=articulos
            Estructura:
            Nombre Campo=id Tipo=dbLong Entero largo, size=4
            Nombre Campo=COMPOSICIO Tipo=dbLongBinary
            Nombre Campo=FOTO Tipo=dbLongBinary
            """);
        var text = Convert.ToBase64String(Encoding.Unicode.GetBytes(composicio));
        var image = Convert.ToBase64String(foto);
        File.WriteAllText(
            Path.Combine(directory.Path, "articulos.csv"),
            $"id|COMPOSICIO|FOTO|\n1|{text}|{image}|\n");
        return directory;
    }

    public static byte[] MinimalBmp()
    {
        var data = new byte[58];
        data[0] = 0x42;
        data[1] = 0x4D;
        BitConverter.GetBytes(58).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(1).CopyTo(data, 18);
        BitConverter.GetBytes(1).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        return data;
    }
}

file static class DbMemoFixture
{
    public static TempDir Create(string text)
    {
        var directory = new TempDir();
        File.WriteAllText(
            Path.Combine(directory.Path, "receta.txt"),
            """
            Nombre Tabla=receta
            Estructura:
            Nombre Campo=id Tipo=dbLong Entero largo, size=4
            Nombre Campo=COMPOSICIO Tipo=dbMemo
            """);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
        File.WriteAllText(
            Path.Combine(directory.Path, "receta.csv"),
            $"id|COMPOSICIO|\n1|{encoded}|\n");
        return directory;
    }
}

file static class AlergenosFixture
{
    private const string Txt = """
        Nombre Tabla=alergenos
        Estructura:
        Nombre Campo=id_alergeno Tipo=dbLong  Entero largo, size=4
        Nombre Campo=DESCRIPCIO Tipo=dbText Texto, size=30
        Nombre Campo=FOTO_activado Tipo=dbLongBinary    Objeto OLE / binario largo
        Indices:
        Nombre Indice: id_alergeno
         Es Primary: False
         Es Unique: True
         Campos:
            - id_alergeno
        """;

    public static TempDir CreateSchemaOnly()
    {
        var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), Txt);
        return directory;
    }

    public static TempDir CreateWithCsv()
    {
        var directory = CreateSchemaOnly();
        File.WriteAllText(
            Path.Combine(directory.Path, "alergenos.csv"),
            "id_alergeno|DESCRIPCIO|FOTO_activado|\n1|GLUTEN||\n2|HUEVOS||\n");
        return directory;
    }
}

file sealed class ScriptedOverwrite : ILegacyImportInteraction
{
    private readonly Queue<bool> _answers;
    private readonly bool _throwIfAsked;

    private ScriptedOverwrite(bool throwIfAsked, params bool[] answers)
    {
        _throwIfAsked = throwIfAsked;
        _answers = new Queue<bool>(answers);
    }

    public static ScriptedOverwrite Unexpected()
    {
        return new(throwIfAsked: true);
    }

    public static ScriptedOverwrite With(params bool[] answers)
    {
        return new(throwIfAsked: false, answers);
    }

    public void Inform(string message)
    {
    }

    public bool ConfirmOverwrite(string tableName)
    {
        if (_throwIfAsked || _answers.Count == 0)
        {
            throw new InvalidOperationException(
                $"No se esperaba confirmación para '{tableName}'.");
        }

        return _answers.Dequeue();
    }
}

file sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = Directory.CreateTempSubdirectory().FullName;
    }

    public string Path { get; }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}

internal sealed class IsolatedSqlDatabase : IAsyncDisposable
{
    private const string MasterConnectionString =
        "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true";

    public static bool IsAvailable { get; } = Probe();

    private IsolatedSqlDatabase(string databaseName, string connectionString)
    {
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public static async Task<IsolatedSqlDatabase> CreateAsync()
    {
        var databaseName = "TPVONE_TESTS_" + Guid.NewGuid().ToString("N");
        Assert.NotEqual("TPVONE", databaseName);
        Assert.StartsWith("TPVONE_TESTS_", databaseName, StringComparison.Ordinal);

        await using (var master = new SqlConnection(MasterConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}];", master)
            {
                CommandTimeout = 60
            };
            await create.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = databaseName
        };
        var database = new IsolatedSqlDatabase(databaseName, builder.ConnectionString);
        try
        {
            await database.ApplyMigrationsAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ListUserTablesAsync()
    {
        const string sql = """
            SELECT name
            FROM sys.tables
            WHERE schema_id = SCHEMA_ID(N'dbo')
            ORDER BY name;
            """;
        var tables = new List<string>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public async Task<(string TypeName, short MaxLength)> ReadColumnTypeAsync(
        string tableName,
        string columnName)
    {
        const string sql = """
            SELECT ty.name, c.max_length
            FROM sys.columns c
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TableName), N'U')
              AND c.name = @ColumnName;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt16(1));
    }

    public async Task<long> CountRowsAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT COUNT_BIG(*) FROM dbo.[{tableName.Replace("]", "]]", StringComparison.Ordinal)}];",
            connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<byte[]?> ReadBinaryAsync(
        string tableName,
        string columnName,
        string idColumn,
        int id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quotedTable = tableName.Replace("]", "]]", StringComparison.Ordinal);
        var quotedColumn = columnName.Replace("]", "]]", StringComparison.Ordinal);
        var quotedId = idColumn.Replace("]", "]]", StringComparison.Ordinal);
        await using var command = new SqlCommand(
            $"SELECT [{quotedColumn}] FROM dbo.[{quotedTable}] WHERE [{quotedId}] = @Id;",
            connection);
        command.Parameters.AddWithValue("@Id", id);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (byte[])value;
    }

    public async Task<string?> ReadNVarCharAsync(
        string tableName,
        string columnName,
        string idColumn,
        int id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quotedTable = tableName.Replace("]", "]]", StringComparison.Ordinal);
        var quotedColumn = columnName.Replace("]", "]]", StringComparison.Ordinal);
        var quotedId = idColumn.Replace("]", "]]", StringComparison.Ordinal);
        await using var command = new SqlCommand(
            $"SELECT [{quotedColumn}] FROM dbo.[{quotedTable}] WHERE [{quotedId}] = @Id;",
            connection);
        command.Parameters.AddWithValue("@Id", id);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<string>> ReadHistoryStatusesAsync(string tableName)
    {
        const string sql = """
            SELECT Status
            FROM dbo.LegacyImportHistory
            WHERE TableName = @TableName
            ORDER BY Id;
            """;
        var statuses = new List<string>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statuses.Add(reader.GetString(0));
        }

        return statuses;
    }

    public async ValueTask DisposeAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        var sql = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await using var command = new SqlCommand(sql, master) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using (var createHistory = new SqlCommand(
                         """
                         IF OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL
                         BEGIN
                             CREATE TABLE dbo.SchemaMigrations
                             (
                                 MigrationId nvarchar(255) NOT NULL PRIMARY KEY,
                                 AppliedAt datetime2 NOT NULL DEFAULT SYSDATETIME()
                             );
                         END
                         """,
                         connection))
        {
            await createHistory.ExecuteNonQueryAsync();
        }

        foreach (var file in Directory.GetFiles(
                     Path.Combine(AppContext.BaseDirectory, "Migrations"),
                     "*.sql").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            foreach (var batch in SplitBatches(await File.ReadAllTextAsync(file)))
            {
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }

            await using var insert = new SqlCommand(
                "INSERT INTO dbo.SchemaMigrations (MigrationId) VALUES (@Id);",
                connection);
            insert.Parameters.AddWithValue("@Id", id);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<string> SplitBatches(string sql)
    {
        var batch = new System.Text.StringBuilder();
        using var reader = new StringReader(sql);
        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var text = batch.ToString().Trim();
                if (text.Length > 0)
                {
                    yield return text;
                }

                batch.Clear();
                continue;
            }

            batch.AppendLine(line);
        }

        var final = batch.ToString().Trim();
        if (final.Length > 0)
        {
            yield return final;
        }
    }

    private static bool Probe()
    {
        try
        {
            using var connection = new SqlConnection(MasterConnectionString);
            connection.Open();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
