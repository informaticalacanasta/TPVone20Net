using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TPVOne.Database;
using TPVOne.LegacyAccess;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging(builder =>
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                });
                builder.SetMinimumLevel(LogLevel.Information);
            })
            .AddSingleton<DatabaseInstaller>()
            .AddSingleton<MigrationRunner>()
            .AddSingleton<LegacyImporterProcess>();

        await using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("TPVOne");

        try
        {
            var command = ParseCommand(args);
            var importOptions = configuration
                .GetSection("LegacyImport")
                .Get<LegacyImportOptions>()
                ?? configuration
                    .GetSection("LegacyAccessImport")
                    .Get<LegacyImportOptions>()
                ?? new LegacyImportOptions();
            if (command.SourceDirectory is not null)
            {
                importOptions.SourceDirectory = command.SourceDirectory;
            }

            if (command.Mode is null)
            {
                var installerOnly = serviceProvider.GetRequiredService<DatabaseInstaller>();
                await installerOnly.EnsureDatabaseExistsAsync();
                var migrationRunnerOnly = serviceProvider.GetRequiredService<MigrationRunner>();
                await migrationRunnerOnly.EnsureSchemaMigrationsAsync();
                await migrationRunnerOnly.ApplyPendingMigrationsAsync();
                logger.LogInformation("TPVOne iniciado correctamente.");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(importOptions.SourceDirectory))
            {
                throw new InvalidOperationException(
                    "Configure LegacyImport:SourceDirectory o use --source.");
            }

            var installer = serviceProvider.GetRequiredService<DatabaseInstaller>();
            await installer.EnsureDatabaseExistsAsync();
            var migrationRunner = serviceProvider.GetRequiredService<MigrationRunner>();
            await migrationRunner.EnsureSchemaMigrationsAsync();
            await migrationRunner.ApplyPendingMigrationsAsync();

            var importer = serviceProvider.GetRequiredService<LegacyImporterProcess>();
            var connection = installer.GetApplicationConnectionString();
            var result = await importer.RunAsync(
                command.Mode,
                importOptions,
                connection);
            return result.IsSuccessful ? 0 : 2;
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "No se pudo inicializar TPVOne.");
            return 1;
        }
    }

    private static LegacyCommand ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return new(null, null);
        }

        string? mode = null;
        string? source = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--analyze":
                case "--analyze-access":
                    mode = "analyze";
                    break;
                case "--import":
                case "--import-access":
                    mode = "import";
                    break;
                case "--source":
                    if (++index >= args.Length)
                    {
                        throw new ArgumentException("Falta el valor para --source.");
                    }

                    source = args[index];
                    break;
                default:
                    if (mode is not null && source is null && !args[index].StartsWith('-'))
                    {
                        source = args[index];
                        break;
                    }

                    throw new ArgumentException(
                        "Uso: TPVOne [--analyze|--import] [--source carpeta]");
            }
        }

        return new(mode, source);
    }

    private sealed record LegacyCommand(string? Mode, string? SourceDirectory);
}
