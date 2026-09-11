using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace TPVOne.Database;

internal sealed class MigrationRunner
{
    private const string MigrationResourceMarker = ".Database.Migrations.";

    private readonly DatabaseInstaller _databaseInstaller;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(
        DatabaseInstaller databaseInstaller,
        ILogger<MigrationRunner> logger)
    {
        _databaseInstaller = databaseInstaller;
        _logger = logger;
    }

    public async Task EnsureSchemaMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SchemaMigrations
                (
                    MigrationId nvarchar(255) NOT NULL,
                    AppliedAt datetime2 NOT NULL
                        CONSTRAINT DF_SchemaMigrations_AppliedAt
                        DEFAULT SYSDATETIME(),

                    CONSTRAINT PK_SchemaMigrations
                        PRIMARY KEY (MigrationId)
                );
            END;
            """;

        await using var connection =
            new SqlConnection(_databaseInstaller.GetApplicationConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplyPendingMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var migrations = LoadEmbeddedMigrations();

        await using var connection =
            new SqlConnection(_databaseInstaller.GetApplicationConnectionString());
        await connection.OpenAsync(cancellationToken);

        var appliedMigrations = await LoadAppliedMigrationsAsync(
            connection,
            cancellationToken);

        foreach (var migration in migrations.Where(
                     migration => !appliedMigrations.Contains(migration.Id)))
        {
            await ApplyMigrationAsync(connection, migration, cancellationToken);
        }
    }

    private static async Task<HashSet<string>> LoadAppliedMigrationsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT MigrationId FROM dbo.SchemaMigrations;";
        var appliedMigrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    private async Task ApplyMigrationAsync(
        SqlConnection connection,
        EmbeddedMigration migration,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Aplicando migración {MigrationId}...", migration.Id);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var batch in SplitSqlBatches(migration.Sql))
            {
                await using var command = new SqlCommand(batch, connection, transaction);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertSql = """
                INSERT INTO dbo.SchemaMigrations (MigrationId)
                VALUES (@MigrationId);
                """;

            await using var insertCommand =
                new SqlCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("@MigrationId", migration.Id);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Migración {MigrationId} aplicada correctamente.",
                migration.Id);
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(
                    rollbackException,
                    "No se pudo revertir la migración {MigrationId}.",
                    migration.Id);
            }

            _logger.LogError(
                exception,
                "Error aplicando migración {MigrationId}.",
                migration.Id);
            throw new InvalidOperationException(
                $"No se pudo aplicar la migración '{migration.Id}'.",
                exception);
        }
    }

    private static IReadOnlyList<EmbeddedMigration> LoadEmbeddedMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.Contains(MigrationResourceMarker, StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name => ReadMigration(assembly, name))
            .OrderBy(migration => migration.Order)
            .ThenBy(migration => migration.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicate = migrations
            .GroupBy(migration => migration.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Hay varias migraciones con el identificador '{duplicate.Key}'.");
        }

        return migrations;
    }

    private static EmbeddedMigration ReadMigration(
        Assembly assembly,
        string resourceName)
    {
        var markerIndex = resourceName.IndexOf(
            MigrationResourceMarker,
            StringComparison.Ordinal);
        var fileName = resourceName[
            (markerIndex + MigrationResourceMarker.Length)..];
        var migrationId = Path.GetFileNameWithoutExtension(fileName);
        var separatorIndex = migrationId.IndexOf('_');

        if (separatorIndex <= 0 ||
            !int.TryParse(migrationId[..separatorIndex], out var order))
        {
            throw new InvalidOperationException(
                $"La migración '{fileName}' no tiene un prefijo numérico válido.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"No se pudo abrir el recurso de migración '{resourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return new EmbeddedMigration(migrationId, order, reader.ReadToEnd());
    }

    private static IEnumerable<string> SplitSqlBatches(string sql)
    {
        var batch = new StringBuilder();
        var inString = false;
        var inBlockComment = false;
        var inBracketIdentifier = false;
        var inQuotedIdentifier = false;

        using var reader = new StringReader(sql);
        while (reader.ReadLine() is { } line)
        {
            if (!inString &&
                !inBlockComment &&
                !inBracketIdentifier &&
                !inQuotedIdentifier &&
                line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var batchSql = batch.ToString().Trim();
                if (batchSql.Length > 0)
                {
                    yield return batchSql;
                }

                batch.Clear();
                continue;
            }

            batch.AppendLine(line);
            UpdateSqlState(
                line,
                ref inString,
                ref inBlockComment,
                ref inBracketIdentifier,
                ref inQuotedIdentifier);
        }

        var finalBatch = batch.ToString().Trim();
        if (finalBatch.Length > 0)
        {
            yield return finalBatch;
        }
    }

    private static void UpdateSqlState(
        string line,
        ref bool inString,
        ref bool inBlockComment,
        ref bool inBracketIdentifier,
        ref bool inQuotedIdentifier)
    {
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            var next = index + 1 < line.Length ? line[index + 1] : '\0';

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (inString)
            {
                if (current == '\'' && next == '\'')
                {
                    index++;
                }
                else if (current == '\'')
                {
                    inString = false;
                }

                continue;
            }

            if (inBracketIdentifier)
            {
                if (current == ']' && next == ']')
                {
                    index++;
                }
                else if (current == ']')
                {
                    inBracketIdentifier = false;
                }

                continue;
            }

            if (inQuotedIdentifier)
            {
                if (current == '"' && next == '"')
                {
                    index++;
                }
                else if (current == '"')
                {
                    inQuotedIdentifier = false;
                }

                continue;
            }

            if (current == '-' && next == '-')
            {
                break;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
            }
            else if (current == '\'')
            {
                inString = true;
            }
            else if (current == '[')
            {
                inBracketIdentifier = true;
            }
            else if (current == '"')
            {
                inQuotedIdentifier = true;
            }
        }
    }

    private sealed record EmbeddedMigration(string Id, int Order, string Sql);
}
