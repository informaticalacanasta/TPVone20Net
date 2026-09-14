using System.Text.Json;
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

            if (accessCommand.Mode is null)
            {
                var installerOnly = serviceProvider.GetRequiredService<DatabaseInstaller>();
                await installerOnly.EnsureDatabaseExistsAsync();
                var migrationRunnerOnly = serviceProvider.GetRequiredService<MigrationRunner>();
                await migrationRunnerOnly.EnsureSchemaMigrationsAsync();
                await migrationRunnerOnly.ApplyPendingMigrationsAsync();
                logger.LogInformation("TPVOne iniciado correctamente.");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(importOptions.SourceDirectory) ||
                string.IsNullOrWhiteSpace(importOptions.ConvertedDirectory))
            {
                throw new InvalidOperationException(
                    "Configure LegacyAccessImport:SourceDirectory y ConvertedDirectory.");
            }

            var installer = serviceProvider.GetRequiredService<DatabaseInstaller>();
            await installer.EnsureDatabaseExistsAsync();
            var migrationRunner = serviceProvider.GetRequiredService<MigrationRunner>();
            await migrationRunner.EnsureSchemaMigrationsAsync();
            await migrationRunner.ApplyPendingMigrationsAsync();

            var importer = serviceProvider.GetRequiredService<LegacyImporterProcess>();
            var connection = installer.GetApplicationConnectionString();

            if (accessCommand.Mode is "analyze" or "plan" or "convert")
            {
                var result = await importer.RunAsync(
                    accessCommand.Mode,
                    importOptions,
                    connection);
                return result.IsSuccessful ? 0 : 2;
            }

            var plan = await importer.RunAsync("plan", importOptions, connection);
            var pending = plan.Plan
                .Where(item => item.Status == PlannedTableStatus.RequiresDecision)
                .ToArray();
            var decisions = new List<TableDecision>();
            foreach (var item in pending)
            {
                var action = PromptDecision(item);
                if (action == ExistingTableAction.Cancel)
                {
                    Console.WriteLine("Importación cancelada. No se ejecutará el lote.");
                    return 2;
                }

                decisions.Add(new(item.SourceHash, item.TableName, action));
            }

            string? decisionsFile = null;
            if (decisions.Count > 0)
            {
                decisionsFile = Path.Combine(
                    Path.GetTempPath(),
                    $"tpvone-decisions-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(
                    decisionsFile,
                    JsonSerializer.Serialize(
                        new DecisionFile { Items = decisions },
                        PipelineJson.Options));
            }

            try
            {
                var imported = await importer.RunAsync(
                    "import",
                    importOptions,
                    connection,
                    decisionsFile);
                return imported.IsSuccessful ? 0 : 2;
            }
            finally
            {
                if (decisionsFile is not null)
                {
                    File.Delete(decisionsFile);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "No se pudo inicializar la base de datos. TPVOne finalizará.");
            return 1;
        }
    }

    private static ExistingTableAction PromptDecision(PlannedTable item)
    {
        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine("TABLA SQL YA EXISTENTE");
        Console.WriteLine("==================================================");
        Console.WriteLine();
        Console.WriteLine("Archivo:");
        Console.WriteLine(item.ConvertedFile);
        Console.WriteLine();
        Console.WriteLine("Tabla:");
        Console.WriteLine(item.TableName);
        Console.WriteLine();
        Console.WriteLine("MDB nuevo:");
        Console.WriteLine($"{item.SourceRowCount} registros");
        Console.WriteLine();
        Console.WriteLine("SQL Server actual:");
        Console.WriteLine($"{item.SqlRowCount} registros");
        Console.WriteLine();
        Console.WriteLine("El origen es diferente al que se importó anteriormente.");
        Console.WriteLine();
        Console.WriteLine("¿Qué desea hacer?");
        Console.WriteLine();
        Console.WriteLine("[1] Omitir (solo esta tabla)");
        Console.WriteLine("[2] Sustituir tabla completa");
        Console.WriteLine("[3] Cancelar importación (lote completo)");

        while (true)
        {
            Console.Write("> ");
            var answer = Console.ReadLine()?.Trim();
            switch (answer)
            {
                case "1":
                    return ExistingTableAction.Skip;
                case "2":
                    return ExistingTableAction.Replace;
                case "3":
                    return ExistingTableAction.Cancel;
            }

            Console.WriteLine("Seleccione 1, 2 o 3.");
        }
    }

    private static AccessCommand ParseAccessCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return new(null, null);
        }

        if (args.Length > 2 ||
            args[0] is not ("--analyze-access" or "--import-access" or "--plan-access" or "--convert-access"))
        {
            throw new ArgumentException(
                "Uso: TPVOne [--analyze-access|--plan-access|--import-access|--convert-access] [carpeta]");
        }

        return new(
            args[0] switch
            {
                "--analyze-access" => "analyze",
                "--plan-access" => "plan",
                "--convert-access" => "convert",
                _ => "import"
            },
            args.Length == 2 ? args[1] : null);
    }

    private sealed record AccessCommand(string? Mode, string? SourceDirectory);
}
