using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Planning;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Utilities;
using TPVOne.LegacyAccessImporter.Conversion;
using TPVOne.LegacyAccessImporter.Database;

namespace TPVOne.LegacyAccessImporter.Import;

internal sealed class LegacyDatabaseImportService
{
    private readonly LegacyAccessImportOptions _options;
    private readonly AccessDatabaseReader _accessReader;
    private readonly AccessToSqlTypeMapper _typeMapper;
    private readonly SqlServerSchemaService? _schemaService;
    private readonly SqlBulkImporter? _bulkImporter;
    private readonly ImportHistoryService? _historyService;
    private readonly LegacyAccessConversionService? _conversionService;
    private readonly string? _connectionString;
    private readonly ILogger<LegacyDatabaseImportService> _logger;

    public LegacyDatabaseImportService(
        LegacyAccessImportOptions options,
        AccessDatabaseReader accessReader,
        AccessToSqlTypeMapper typeMapper,
        SqlServerSchemaService? schemaService,
        SqlBulkImporter? bulkImporter,
        ImportHistoryService? historyService,
        LegacyAccessConversionService? conversionService,
        string? connectionString,
        ILogger<LegacyDatabaseImportService> logger)
    {
        _options = options;
        _accessReader = accessReader;
        _typeMapper = typeMapper;
        _schemaService = schemaService;
        _bulkImporter = bulkImporter;
        _historyService = historyService;
        _conversionService = conversionService;
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<PipelineResult> AnalyzeAsync(
        CancellationToken cancellationToken = default)
    {
        var conversion = await ConvertIfConfiguredAsync(cancellationToken);
        var databases = await ReadConvertedDatabasesAsync(conversion, cancellationToken);
        return new(
            "analyze",
            conversion,
            databases.Databases,
            [],
            [],
            databases.Warnings,
            conversion.Errors.Concat(databases.Errors).ToArray());
    }

    public async Task<PipelineResult> ConvertAsync(
        CancellationToken cancellationToken = default)
    {
        var conversion = await ConvertIfConfiguredAsync(cancellationToken);
        return new(
            "convert",
            conversion,
            [],
            [],
            [],
            [],
            conversion.Errors);
    }

    public async Task<PipelineResult> PlanAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureImportServices();
        var conversion = await ConvertIfConfiguredAsync(cancellationToken);
        var databases = await ReadConvertedDatabasesAsync(conversion, cancellationToken);
        var plan = await BuildPlanAsync(databases.Databases, cancellationToken);
        PrintPlan(plan);
        return new(
            "plan",
            conversion,
            databases.Databases,
            plan,
            [],
            databases.Warnings,
            conversion.Errors.Concat(databases.Errors).ToArray());
    }

    public async Task<PipelineResult> ImportAsync(
        IReadOnlyList<TableDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        EnsureImportServices();
        var conversion = await ConvertIfConfiguredAsync(cancellationToken);
        var databases = await ReadConvertedDatabasesAsync(conversion, cancellationToken);
        var plan = await BuildPlanAsync(databases.Databases, cancellationToken);
        PrintPlan(plan);

        if (decisions.Any(decision => decision.Action == ExistingTableAction.Cancel) ||
            (plan.Any(item => item.Status == PlannedTableStatus.RequiresDecision) &&
             decisions.Count == 0))
        {
            if (plan.Any(item => item.Status == PlannedTableStatus.RequiresDecision) &&
                decisions.Count == 0)
            {
                return new(
                    "plan",
                    conversion,
                    databases.Databases,
                    plan,
                    [],
                    databases.Warnings,
                    conversion.Errors.Concat(databases.Errors).ToArray());
            }

            _logger.LogInformation("[USER] Cancelar importación. Lote no ejecutado.");
            return new(
                "import",
                conversion,
                databases.Databases,
                plan,
                [],
                databases.Warnings,
                ["Importación cancelada por el usuario. No se ejecutó ninguna tabla pendiente."]);
        }

        var results = new List<TableImportResult>();
        var errors = conversion.Errors.Concat(databases.Errors).ToList();
        var workflow = new SafeReplacementWorkflow();

        foreach (var item in plan)
        {
            var database = databases.Databases.First(candidate =>
                string.Equals(candidate.SourceHash, item.SourceHash, StringComparison.OrdinalIgnoreCase) &&
                candidate.Tables.Any(table =>
                    string.Equals(table.Name, item.TableName, StringComparison.OrdinalIgnoreCase)));
            var table = database.Tables.First(candidate =>
                string.Equals(candidate.Name, item.TableName, StringComparison.OrdinalIgnoreCase));

            if (item.Status == PlannedTableStatus.Collision)
            {
                results.Add(await RecordAsync(
                    database,
                    table,
                    ImportStatus.Conflict,
                    item.SqlRowCount,
                    0,
                    false,
                    item.Message,
                    cancellationToken));
                errors.Add($"{item.OriginalFile}: {item.Message}");
                continue;
            }

            if (item.Status == PlannedTableStatus.SkipAlreadyImported)
            {
                _logger.LogInformation(
                    "[IMPORT] {Table} ya importada desde el mismo hash. OMITIDA.",
                    item.TableName);
                results.Add(await RecordAsync(
                    database,
                    table,
                    ImportStatus.SkippedAlreadyImported,
                    item.SqlRowCount,
                    0,
                    false,
                    item.Message,
                    cancellationToken));
                continue;
            }

            if (item.Status == PlannedTableStatus.RequiresDecision)
            {
                var decision = decisions.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourceHash, item.SourceHash, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.TableName, item.TableName, StringComparison.OrdinalIgnoreCase));
                if (decision is null || decision.Action == ExistingTableAction.Skip)
                {
                    _logger.LogInformation("[USER] {Table} = Skip", item.TableName);
                    results.Add(await RecordAsync(
                        database,
                        table,
                        ImportStatus.SkippedByUser,
                        item.SqlRowCount,
                        0,
                        false,
                        "Omitida por el usuario.",
                        cancellationToken));
                    continue;
                }

                if (decision.Action == ExistingTableAction.Cancel)
                {
                    results.Add(await RecordAsync(
                        database,
                        table,
                        ImportStatus.Cancelled,
                        item.SqlRowCount,
                        0,
                        false,
                        "Lote cancelado por el usuario.",
                        cancellationToken));
                    errors.Add("Importación cancelada por el usuario.");
                    break;
                }
            }

            try
            {
                foreach (var column in table.Columns)
                {
                    _typeMapper.Map(column);
                }

                var staging = $"__tpv_stg_{Guid.NewGuid():N}";
                var backup = $"__tpv_old_{Guid.NewGuid():N}";
                var operations = new SqlReplacementOperations(
                    _connectionString!,
                    _schemaService!,
                    _bulkImporter!,
                    database,
                    table,
                    _options.CommandTimeoutSeconds);

                if (item.SqlExists)
                {
                    _logger.LogInformation("[USER] {Table} = Replace", item.TableName);
                    _logger.LogInformation("[STAGING] Creando {Staging}", staging);
                }
                else
                {
                    _logger.LogInformation("[SQL] Creando dbo.{Table} mediante staging", item.TableName);
                }

                var imported = await workflow.ReplaceOrCreateAsync(
                    operations,
                    table,
                    item.TableName,
                    staging,
                    backup,
                    cancellationToken);

                var status = item.SqlExists ? ImportStatus.Replaced : ImportStatus.Success;
                _logger.LogInformation(
                    "[IMPORT] {Imported}/{Source}",
                    imported,
                    table.RowCount);
                _logger.LogInformation("[VALIDATE] OK");
                if (item.SqlExists)
                {
                    _logger.LogInformation("[REPLACE] dbo.{Table}", item.TableName);
                }

                results.Add(await RecordAsync(
                    database,
                    table,
                    status,
                    item.SqlRowCount,
                    imported,
                    !item.SqlExists,
                    null,
                    cancellationToken));
                _logger.LogInformation("[HISTORY] registrado");
            }
            catch (Exception exception)
            {
                var error =
                    $"Archivo: {item.OriginalFile}; Tabla: {item.TableName}; " +
                    $"Operación: {(item.SqlExists ? "sustitución" : "creación")}; " +
                    $"Mensaje: {RootMessage(exception)}";
                errors.Add(error);
                results.Add(await RecordAsync(
                    database,
                    table,
                    ImportStatus.Failed,
                    item.SqlRowCount,
                    0,
                    false,
                    error,
                    CancellationToken.None));
                _logger.LogError(exception, "[ERROR] {Error}", error);
            }
        }

        return new(
            "import",
            conversion,
            databases.Databases,
            plan,
            results,
            databases.Warnings,
            errors);
    }

    private async Task<ConversionBatchResult> ConvertIfConfiguredAsync(
        CancellationToken cancellationToken)
    {
        if (_conversionService is null)
        {
            return new(0, 0, 0, 0, 0, [], []);
        }

        return await _conversionService.ConvertAllAsync(cancellationToken);
    }

    private async Task<(IReadOnlyList<AccessDatabaseSchema> Databases, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)>
        ReadConvertedDatabasesAsync(
            ConversionBatchResult conversion,
            CancellationToken cancellationToken)
    {
        var files = conversion.Items
            .Where(item => item.ConvertedFile is not null && item.Status is not "Failed")
            .Select(item => item)
            .ToArray();
        if (files.Length == 0 && conversion.FilesFound == 0)
        {
            var fallback = Directory.Exists(_options.ConvertedDirectory)
                ? LegacyFileScanner.FindMdbFiles(_options.ConvertedDirectory)
                : [];
            var databasesFromFallback = new List<AccessDatabaseSchema>();
            var warnings = new List<string>();
            var errors = new List<string>();
            foreach (var file in fallback)
            {
                await ReadOneAsync(file, file, databasesFromFallback, warnings, errors, cancellationToken);
            }

            return (databasesFromFallback, warnings, errors);
        }

        var databases = new List<AccessDatabaseSchema>();
        var conversionWarnings = new List<string>();
        var conversionErrors = new List<string>();
        foreach (var item in files)
        {
            await ReadOneAsync(
                item.ConvertedFile!,
                item.OriginalFile,
                databases,
                conversionWarnings,
                conversionErrors,
                cancellationToken,
                item.SourceHash);
        }

        return (databases, conversionWarnings, conversionErrors);
    }

    private async Task ReadOneAsync(
        string convertedFile,
        string originalFile,
        ICollection<AccessDatabaseSchema> databases,
        ICollection<string> warnings,
        ICollection<string> errors,
        CancellationToken cancellationToken,
        string? originalHash = null)
    {
        _logger.LogInformation("[ACCESS] Analizando {File}", convertedFile);
        try
        {
            var database = await _accessReader.ReadSchemaAsync(
                convertedFile,
                cancellationToken: cancellationToken);
            database = database with
            {
                OriginalFilePath = originalFile,
                SourceHash = originalHash ?? database.SourceHash
            };
            databases.Add(database);
            foreach (var warning in database.Warnings)
            {
                warnings.Add($"{Path.GetFileName(convertedFile)}: {warning}");
            }
            _logger.LogInformation(
                "[ACCESS] Proveedor seleccionado: {Provider}",
                database.Provider);
            foreach (var table in database.Tables)
            {
                _logger.LogInformation(
                    "[ACCESS] Tabla encontrada: {Table}; Columnas: {Columns}; Registros: {Rows}",
                    table.Name,
                    table.Columns.Count,
                    table.RowCount);
            }
        }
        catch (Exception exception)
        {
            var message = $"Archivo: {originalFile}; Operación: análisis Access; " +
                $"Mensaje: {RootMessage(exception)}";
            errors.Add(message);
            _logger.LogError(exception, "[ACCESS] {Message}", message);
        }
    }

    private async Task<IReadOnlyList<PlannedTable>> BuildPlanAsync(
        IReadOnlyList<AccessDatabaseSchema> databases,
        CancellationToken cancellationToken)
    {
        var successful = await _historyService!.LoadSuccessfulImportsAsync(cancellationToken);
        var sqlCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableName in databases
                     .SelectMany(database => database.Tables.Select(table => table.Name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await _schemaService!.TableExistsAsync(tableName, cancellationToken))
            {
                sqlCounts[tableName] = await _schemaService.CountRowsAsync(
                    tableName,
                    cancellationToken);
            }
        }

        var plan = ImportPlanBuilder.Build(databases, successful, sqlCounts);
        foreach (var item in plan)
        {
            _logger.LogInformation(
                "[PLAN] {Table}; SQL {Sql}; {Status}",
                item.TableName,
                item.SqlExists ? $"existe, {item.SqlRowCount} filas" : "no existe",
                item.Status);
        }

        return plan;
    }

    private async Task<TableImportResult> RecordAsync(
        AccessDatabaseSchema database,
        AccessTableSchema table,
        ImportStatus status,
        long? previousRows,
        long imported,
        bool created,
        string? error,
        CancellationToken cancellationToken)
    {
        var id = await _historyService!.StartAsync(
            database,
            table,
            status,
            previousRows,
            cancellationToken);
        await _historyService.FinishAsync(
            id,
            status,
            imported,
            error,
            cancellationToken);
        return new(
            database.FilePath,
            database.OriginalFilePath ?? database.FilePath,
            table.Name,
            status,
            created,
            table.RowCount,
            imported,
            previousRows,
            error);
    }

    private static void PrintPlan(IReadOnlyList<PlannedTable> plan)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("PLAN DE IMPORTACIÓN");
        Console.WriteLine("==================================================");
        foreach (var group in plan.GroupBy(
                     item => item.OriginalFile,
                     StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine($"Archivo: {group.Key}");
            Console.WriteLine($"Hash: {group.First().SourceHash}");
            Console.WriteLine($"Tablas: {group.Count()}");
            foreach (var item in group)
            {
                Console.WriteLine();
                Console.WriteLine(item.TableName);
                Console.WriteLine($"MDB: {item.SourceRowCount} filas");
                Console.WriteLine(
                    item.SqlExists
                        ? $"SQL: existe, {item.SqlRowCount} filas"
                        : "SQL: no existe");
                Console.WriteLine($"Estado: {Describe(item)}");
            }
        }
    }

    private static string Describe(PlannedTable item)
    {
        return item.Status switch
        {
            PlannedTableStatus.Create => "CREAR",
            PlannedTableStatus.SkipAlreadyImported => "OMITIR AUTOMÁTICAMENTE",
            PlannedTableStatus.RequiresDecision => "REQUIERE DECISIÓN",
            PlannedTableStatus.Collision => "COLISIÓN",
            _ => item.Status.ToString()
        };
    }

    private void EnsureImportServices()
    {
        if (_schemaService is null ||
            _bulkImporter is null ||
            _historyService is null ||
            _connectionString is null)
        {
            throw new InvalidOperationException(
                "Los servicios SQL no están configurados para planificar o importar.");
        }
    }

    private static string RootMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
