using System.Text.Json;
using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccessImporter.Database;
using TPVOne.LegacyAccessImporter.Import;

namespace TPVOne.LegacyAccessImporter;

internal static class Program
{
    private const string ResultPrefix = "TPVONE_RESULT_JSON:";

    private static async Task<int> Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(options => options.SingleLine = true));

        try
        {
            var arguments = ParseArguments(args);
            var options = new LegacyAccessImportOptions
            {
                SourceDirectory = arguments.SourceDirectory,
                BatchSize = arguments.BatchSize,
                CommandTimeoutSeconds = arguments.CommandTimeoutSeconds,
                ForceImport = arguments.ForceImport
            };
            var detector = new AccessProviderDetector();
            var accessReader = new AccessDatabaseReader(detector);
            var typeMapper = new AccessToSqlTypeMapper();
            var comparison = new SchemaComparisonService(typeMapper);

            SqlServerSchemaService? schemaService = null;
            SqlBulkImporter? bulkImporter = null;
            ImportHistoryService? historyService = null;
            if (arguments.Mode == "import")
            {
                var connectionString =
                    Environment.GetEnvironmentVariable("TPVONE_SQL_CONNECTION")
                    ?? throw new InvalidOperationException(
                        "No se recibió la conexión segura a TPVONE.");
                schemaService = new(
                    connectionString,
                    typeMapper,
                    options.CommandTimeoutSeconds);
                bulkImporter = new(
                    connectionString,
                    accessReader,
                    schemaService,
                    options);
                historyService = new(
                    connectionString,
                    options.CommandTimeoutSeconds);
            }

            var service = new LegacyDatabaseImportService(
                options,
                accessReader,
                typeMapper,
                comparison,
                schemaService,
                bulkImporter,
                historyService,
                loggerFactory.CreateLogger<LegacyDatabaseImportService>());
            var result = arguments.Mode == "analyze"
                ? await service.AnalyzeAsync()
                : await service.ImportAsync();

            PrintSummary(result);
            Console.WriteLine(ResultPrefix + JsonSerializer.Serialize(result));
            return result.IsSuccessful ? 0 : 2;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("LegacyAccessImporter")
                .LogCritical(exception, "El importador legacy no pudo ejecutarse.");
            return 1;
        }
    }

    private static Arguments ParseArguments(string[] args)
    {
        string? mode = null;
        string? source = null;
        var batchSize = 5000;
        var timeout = 120;
        var force = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode":
                    mode = ReadValue(args, ref index);
                    break;
                case "--source":
                    source = ReadValue(args, ref index);
                    break;
                case "--batch-size":
                    batchSize = int.Parse(ReadValue(args, ref index));
                    break;
                case "--timeout":
                    timeout = int.Parse(ReadValue(args, ref index));
                    break;
                case "--force":
                    force = bool.Parse(ReadValue(args, ref index));
                    break;
                default:
                    throw new ArgumentException($"Argumento desconocido: {args[index]}");
            }
        }

        if (mode is not ("analyze" or "import"))
        {
            throw new ArgumentException("--mode debe ser analyze o import.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (batchSize <= 0 || timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "BatchSize y timeout deben ser positivos.");
        }

        return new(mode, source, batchSize, timeout, force);
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"Falta el valor para {args[index - 1]}.");
        }

        return args[index];
    }

    private static void PrintSummary(LegacyOperationResult result)
    {
        if (result.IsAnalysis)
        {
            foreach (var database in result.Databases)
            {
                Console.WriteLine($"ACCESS: {Path.GetFileName(database.FilePath)}");
                Console.WriteLine($"Proveedor: {database.Provider}");
                foreach (var table in database.Tables)
                {
                    Console.WriteLine(
                        $"  Tabla: {table.Name} | Columnas: {table.Columns.Count} | Registros: {table.RowCount}");
                    foreach (var column in table.Columns.OrderBy(column => column.Ordinal))
                    {
                        Console.WriteLine(
                            $"    {column.Ordinal}: {column.Name} | " +
                            $"{column.ProviderType} | tamaño={column.MaxLength?.ToString() ?? "-"} | " +
                            $"nullable={column.IsNullable} | auto={column.IsAutoIncrement}");
                    }
                }
            }
        }

        Console.WriteLine("========================================");
        Console.WriteLine(result.IsAnalysis
            ? "RESUMEN DE ANÁLISIS"
            : "RESUMEN DE IMPORTACIÓN");
        Console.WriteLine("========================================");
        Console.WriteLine($"MDB encontrados: {result.FilesFound}");
        Console.WriteLine($"MDB abiertos correctamente: {result.FilesOpened}");
        Console.WriteLine($"MDB con error: {result.FilesWithErrors}");
        Console.WriteLine($"Tablas de usuario: {result.UserTables}");
        Console.WriteLine($"Columnas totales: {result.TotalColumns}");
        Console.WriteLine($"Registros origen: {result.TotalSourceRows}");

        if (!result.IsAnalysis)
        {
            Console.WriteLine(
                $"Tablas creadas: {result.Tables.Count(table => table.TableCreated)}");
            Console.WriteLine(
                $"Tablas importadas: {result.Tables.Count(table => table.Status == ImportStatus.Success)}");
            Console.WriteLine(
                $"Tablas omitidas: {result.Tables.Count(table => table.Status == ImportStatus.SkippedAlreadyImported)}");
            Console.WriteLine(
                $"Tablas con error: {result.Tables.Count(table => table.Status is ImportStatus.Failed or ImportStatus.Conflict)}");
            Console.WriteLine(
                $"Registros importados: {result.Tables.Sum(table => table.ImportedRowCount)}");
        }

        Console.WriteLine($"Advertencias: {result.Warnings.Count}");
        Console.WriteLine($"Errores: {result.Errors.Count}");
        Console.WriteLine(result.IsSuccessful
            ? "Resultado: SUCCESS"
            : "Resultado: COMPLETED WITH ERRORS");
    }

    private sealed record Arguments(
        string Mode,
        string SourceDirectory,
        int BatchSize,
        int CommandTimeoutSeconds,
        bool ForceImport);
}
