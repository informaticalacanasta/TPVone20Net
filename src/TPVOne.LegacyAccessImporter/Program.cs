using System.Text.Json;
using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccessImporter.Conversion;
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
                ConvertedDirectory = arguments.ConvertedDirectory,
                BatchSize = arguments.BatchSize,
                CommandTimeoutSeconds = arguments.CommandTimeoutSeconds
            };
            var detector = new AccessProviderDetector();
            var accessReader = new AccessDatabaseReader(detector);
            var typeMapper = new AccessToSqlTypeMapper();

            SqlServerSchemaService? schemaService = null;
            SqlBulkImporter? bulkImporter = null;
            ImportHistoryService? historyService = null;
            LegacyAccessConversionService? conversionService = null;
            string? connectionString = null;
            if (arguments.Mode is "analyze" or "plan" or "import" or "convert")
            {
                connectionString =
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
                conversionService = new(
                    options,
                    new JetEngineAccessConverter(
                        accessReader,
                        loggerFactory.CreateLogger<JetEngineAccessConverter>()),
                    accessReader,
                    new ConversionHistoryService(
                        connectionString,
                        options.CommandTimeoutSeconds),
                    loggerFactory.CreateLogger<LegacyAccessConversionService>());
            }

            var service = new LegacyDatabaseImportService(
                options,
                accessReader,
                typeMapper,
                schemaService,
                bulkImporter,
                historyService,
                conversionService,
                connectionString,
                loggerFactory.CreateLogger<LegacyDatabaseImportService>());

            var result = arguments.Mode switch
            {
                "analyze" => await service.AnalyzeAsync(),
                "plan" => await service.PlanAsync(),
                "import" => await service.ImportAsync(LoadDecisions(arguments.DecisionsFile)),
                "convert" => await service.ConvertAsync(),
                _ => throw new ArgumentException($"Modo no soportado: {arguments.Mode}")
            };

            PrintSummary(result);
            Console.WriteLine(
                ResultPrefix + JsonSerializer.Serialize(result, PipelineJson.Options));
            return result.IsSuccessful ? 0 : 2;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("LegacyAccessImporter")
                .LogCritical(exception, "El importador legacy no pudo ejecutarse.");
            return 1;
        }
    }

    private static IReadOnlyList<TableDecision> LoadDecisions(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<DecisionFile>(json, PipelineJson.Options)
            ?? new DecisionFile();
        return file.Items;
    }

    private static Arguments ParseArguments(string[] args)
    {
        string? mode = null;
        string? source = null;
        string? converted = null;
        string? decisions = null;
        var batchSize = 5000;
        var timeout = 120;

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
                case "--converted":
                    converted = ReadValue(args, ref index);
                    break;
                case "--decisions":
                    decisions = ReadValue(args, ref index);
                    break;
                case "--batch-size":
                    batchSize = int.Parse(ReadValue(args, ref index));
                    break;
                case "--timeout":
                    timeout = int.Parse(ReadValue(args, ref index));
                    break;
                default:
                    throw new ArgumentException($"Argumento desconocido: {args[index]}");
            }
        }

        if (mode is not ("analyze" or "plan" or "import" or "convert"))
        {
            throw new ArgumentException("--mode debe ser analyze, plan, import o convert.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(converted);
        if (batchSize <= 0 || timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "BatchSize y timeout deben ser positivos.");
        }

        return new(mode, source, converted, decisions, batchSize, timeout);
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"Falta el valor para {args[index - 1]}.");
        }

        return args[index];
    }

    private static void PrintSummary(PipelineResult result)
    {
        if (result.Kind == "analyze")
        {
            foreach (var database in result.Databases)
            {
                Console.WriteLine(
                    $"ACCESS: {Path.GetFileName(database.OriginalFilePath ?? database.FilePath)}");
                Console.WriteLine($"Proveedor: {database.Provider}");
                foreach (var table in database.Tables)
                {
                    Console.WriteLine(
                        $"  Tabla: {table.Name} | Columnas: {table.Columns.Count} | Registros: {table.RowCount}");
                }
            }
        }

        if (result.Kind == "convert" && result.Conversion is not null)
        {
            foreach (var item in result.Conversion.Items)
            {
                Console.WriteLine(
                    $"{Path.GetFileName(item.OriginalFile)} | {item.Action} | {item.Status} | {item.Message}");
            }
        }

        Console.WriteLine("==================================================");
        Console.WriteLine("RESUMEN");
        Console.WriteLine("==================================================");
        if (result.Conversion is not null)
        {
            Console.WriteLine($"MDB encontrados:                  {result.Conversion.FilesFound}");
            Console.WriteLine($"MDB convertidos nuevos:           {result.Conversion.Converted}");
            Console.WriteLine($"MDB copiados compatibles:         {result.Conversion.Copied}");
            Console.WriteLine($"MDB conversión omitida por hash:  {result.Conversion.Skipped}");
            Console.WriteLine($"MDB con error de conversión:      {result.Conversion.Failed}");
        }

        if (result.Kind != "convert")
        {
            Console.WriteLine($"Tablas encontradas:              {result.UserTables}");
            Console.WriteLine(
                $"Tablas nuevas creadas:           {result.Tables.Count(table => table.Status == ImportStatus.Success)}");
            Console.WriteLine(
                $"Tablas sustituidas:               {result.Tables.Count(table => table.Status == ImportStatus.Replaced)}");
            Console.WriteLine(
                $"Tablas omitidas mismo hash:      {result.Tables.Count(table => table.Status == ImportStatus.SkippedAlreadyImported)}");
            Console.WriteLine(
                $"Tablas omitidas por usuario:      {result.Tables.Count(table => table.Status == ImportStatus.SkippedByUser)}");
            Console.WriteLine(
                $"Tablas con error:                 {result.Tables.Count(table => table.Status is ImportStatus.Failed or ImportStatus.Conflict)}");
            Console.WriteLine(
                $"Registros importados:        {result.Tables.Sum(table => table.ImportedRowCount)}");
        }
        Console.WriteLine(result.IsSuccessful
            ? "Resultado: OK"
            : "Resultado: COMPLETED WITH ERRORS");
    }

    private sealed record Arguments(
        string Mode,
        string SourceDirectory,
        string ConvertedDirectory,
        string? DecisionsFile,
        int BatchSize,
        int CommandTimeoutSeconds);
}
