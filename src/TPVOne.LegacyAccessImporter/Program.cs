using System.Text.Json;
using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Mapping;
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
            var options = new LegacyImportOptions
            {
                SourceDirectory = arguments.SourceDirectory,
                BatchSize = arguments.BatchSize,
                CommandTimeoutSeconds = arguments.CommandTimeoutSeconds,
                ForceImport = arguments.ForceImport,
                DefaultEncoding = arguments.DefaultEncoding,
                Delimiter = arguments.Delimiter
            };
            var typeMapper = new DaoToSqlTypeMapper();
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
                bulkImporter = new(connectionString, schemaService, options);
                historyService = new(connectionString, options.CommandTimeoutSeconds);
            }

            var service = new LegacyDatabaseImportService(
                options,
                typeMapper,
                comparison,
                schemaService,
                bulkImporter,
                historyService,
                loggerFactory.CreateLogger<LegacyDatabaseImportService>());

            var result = arguments.Mode switch
            {
                "analyze" => await service.AnalyzeAsync(),
                "import" => await service.ImportAsync(),
                _ => throw new ArgumentException($"Modo no soportado: {arguments.Mode}")
            };

            PrintSummary(result);
            Console.WriteLine(
                ResultPrefix + JsonSerializer.Serialize(result, PipelineJson.Options));
            return result.IsSuccessful ? 0 : 2;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("LegacyImporter")
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
        var forceImport = false;
        var encoding = "windows-1252";
        var delimiter = "|";

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
                    forceImport = true;
                    break;
                case "--encoding":
                    encoding = ReadValue(args, ref index);
                    break;
                case "--delimiter":
                    delimiter = ReadValue(args, ref index);
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

        return new(mode, source, batchSize, timeout, forceImport, encoding, delimiter);
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
        Console.WriteLine("==================================================");
        Console.WriteLine("RESUMEN");
        Console.WriteLine("==================================================");
        Console.WriteLine($"Definiciones TXT encontradas:    {result.SchemaDefinitionsFound}");
        Console.WriteLine($"Tablas legacy válidas:           {result.ValidLegacyTables}");
        Console.WriteLine($"Tablas sin CSV:                  {result.TablesWithoutData}");
        Console.WriteLine($"CSV asociados:                   {result.AssociatedDataSources}");
        Console.WriteLine($"CSV huérfanos:                   {result.OrphanDataSources}");
        Console.WriteLine($"Columnas:                        {result.Columns}");
        Console.WriteLine($"Registros disponibles:           {result.AvailableRecords}");
        Console.WriteLine($"Advertencias:                    {result.Warnings.Count}");
        Console.WriteLine($"Errores:                         {result.Errors.Count}");
        if (result.Kind == "import")
        {
            Console.WriteLine(
                $"Tablas creadas:                   {result.Tables.Count(table => table.SchemaStatus == SchemaStatus.Created)}");
            Console.WriteLine(
                $"Datos importados:                 {result.Tables.Count(table => table.DataStatus == DataStatus.Imported)}");
            Console.WriteLine(
                $"Datos no disponibles:             {result.Tables.Count(table => table.DataStatus == DataStatus.NotAvailable)}");
            Console.WriteLine(
                $"Datos ya importados:              {result.Tables.Count(table => table.DataStatus == DataStatus.AlreadyImported)}");
            Console.WriteLine(
                $"Filas importadas:                 {result.Tables.Sum(table => table.ImportedRowCount)}");
        }

        Console.WriteLine(result.IsSuccessful
            ? "Resultado: OK"
            : "Resultado: COMPLETED WITH ERRORS");
    }

    private sealed record Arguments(
        string Mode,
        string SourceDirectory,
        int BatchSize,
        int CommandTimeoutSeconds,
        bool ForceImport,
        string DefaultEncoding,
        string Delimiter);
}
