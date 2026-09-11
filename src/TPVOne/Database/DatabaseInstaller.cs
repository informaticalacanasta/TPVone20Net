using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TPVOne.Database;

internal sealed partial class DatabaseInstaller
{
    public const string DatabaseName = "TPVONE";

    private readonly string _serverConnectionString;
    private readonly ILogger<DatabaseInstaller> _logger;

    public DatabaseInstaller(
        IConfiguration configuration,
        ILogger<DatabaseInstaller> logger)
    {
        _serverConnectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException(
                "No se ha configurado ConnectionStrings:SqlServer.");
        _logger = logger;
    }

    public async Task EnsureDatabaseExistsAsync(
        CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(DatabaseName);

        var masterBuilder = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = "master"
        };

        _logger.LogInformation(
            "Comprobando existencia de base de datos {DatabaseName}...",
            DatabaseName);

        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT DB_ID(@DatabaseName);";
        existsCommand.Parameters.AddWithValue("@DatabaseName", DatabaseName);

        var databaseId = await existsCommand.ExecuteScalarAsync(cancellationToken);
        if (databaseId is not null && databaseId is not DBNull)
        {
            _logger.LogInformation(
                "La base de datos {DatabaseName} ya existe.",
                DatabaseName);
            return;
        }

        _logger.LogInformation(
            "La base de datos {DatabaseName} no existe.",
            DatabaseName);
        _logger.LogInformation("Creando {DatabaseName}...", DatabaseName);

        var escapedDatabaseName = DatabaseName.Replace("]", "]]", StringComparison.Ordinal);
        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = $"CREATE DATABASE [{escapedDatabaseName}];";
        createCommand.CommandTimeout = 60;
        await createCommand.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "Base de datos {DatabaseName} creada correctamente.",
            DatabaseName);
    }

    public string GetApplicationConnectionString()
    {
        var applicationBuilder = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = DatabaseName
        };

        return applicationBuilder.ConnectionString;
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!DatabaseNameRegex().IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                $"El nombre de base de datos '{databaseName}' no es válido.");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseNameRegex();
}
