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
            var accessCommand = ParseAccessCommand(args);
            var importOptions = configuration
                .GetSection("LegacyAccessImport")
                .Get<LegacyAccessImportOptions>()
                ?? new LegacyAccessImportOptions();
            if (accessCommand.SourceDirectory is not null)
            {
                importOptions.SourceDirectory = accessCommand.SourceDirectory;
            }

            if (accessCommand.Mode == "analyze")
            {
                return await serviceProvider
                    .GetRequiredService<LegacyImporterProcess>()
                    .RunAsync("analyze", importOptions, null);
            }

            var installer = serviceProvider.GetRequiredService<DatabaseInstaller>();
            await installer.EnsureDatabaseExistsAsync();

            var migrationRunner = serviceProvider.GetRequiredService<MigrationRunner>();
            await migrationRunner.EnsureSchemaMigrationsAsync();
            await migrationRunner.ApplyPendingMigrationsAsync();

            if (accessCommand.Mode == "import")
            {
                return await serviceProvider
                    .GetRequiredService<LegacyImporterProcess>()
                    .RunAsync(
                        "import",
                        importOptions,
                        installer.GetApplicationConnectionString());
            }

            logger.LogInformation("TPVOne iniciado correctamente.");
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "No se pudo inicializar la base de datos. TPVOne finalizará.");
            return 1;
        }
    }

    private static AccessCommand ParseAccessCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return new(null, null);
        }

        if (args.Length > 2 ||
            args[0] is not ("--analyze-access" or "--import-access"))
        {
            throw new ArgumentException(
                "Uso: TPVOne [--analyze-access|--import-access] [carpeta]");
        }

        return new(
            args[0] == "--analyze-access" ? "analyze" : "import",
            args.Length == 2 ? args[1] : null);
    }

    private sealed record AccessCommand(string? Mode, string? SourceDirectory);
}
