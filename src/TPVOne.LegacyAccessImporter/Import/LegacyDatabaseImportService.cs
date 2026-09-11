using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Utilities;
using TPVOne.LegacyAccessImporter.Database;

namespace TPVOne.LegacyAccessImporter.Import;

internal sealed class LegacyDatabaseImportService
{
    private readonly LegacyAccessImportOptions _options;
    private readonly AccessDatabaseReader _accessReader;
    private readonly AccessToSqlTypeMapper _typeMapper;
    private readonly SchemaComparisonService _comparisonService;
    private readonly SqlServerSchemaService? _schemaService;
    private readonly SqlBulkImporter? _bulkImporter;
    private readonly ImportHistoryService? _historyService;
    private readonly ILogger<LegacyDatabaseImportService> _logger;

    public LegacyDatabaseImportService(
        LegacyAccessImportOptions options,
        AccessDatabaseReader accessReader,
        AccessToSqlTypeMapper typeMapper,
        SchemaComparisonService comparisonService,
        SqlServerSchemaService? schemaService,
        SqlBulkImporter? bulkImporter,
        ImportHistoryService? historyService,
        ILogger<LegacyDatabaseImportService> logger)
    {
        _options = options;
        _accessReader = accessReader;
        _typeMapper = typeMapper;
        _comparisonService = comparisonService;
        _schemaService = schemaService;
        _bulkImporter = bulkImporter;
        _historyService = historyService;
        _logger = logger;
    }

    public async Task<LegacyOperationResult> AnalyzeAsync(
        CancellationToken cancellationToken = default)
    {
        var files = LegacyFileScanner.FindMdbFiles(_options.SourceDirectory);
        var databases = new List<AccessDatabaseSchema>();
        var warnings = new List<string>();
        var errors = new List<string>();

        foreach (var file in files)
        {
            _logger.LogInformation("[ACCESS] Analizando {File}", file);
            try
            {
                var database = await _accessReader.ReadSchemaAsync(
                    file,
                    cancellationToken: cancellationToken);
                databases.Add(database);
                warnings.AddRange(database.Warnings.Select(warning =>
                    $"{Path.GetFileName(file)}: {warning}"));

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
                var message = $"Archivo: {file}{Environment.NewLine}" +
                    $"Operación: análisis Access{Environment.NewLine}" +
                    $"Mensaje: {exception.Message}";
                errors.Add(message);
                _logger.LogError(exception, "[ACCESS] {Message}", message);
            }
        }

        return new(
            true,
            files.Count,
            databases.Count,
            files.Count - databases.Count,
            databases.Sum(database => database.Tables.Count),
            databases.Sum(database => database.Tables.Sum(table => table.Columns.Count)),
            databases.Sum(database => database.Tables.Sum(table => table.RowCount)),
            databases,
            [],
            warnings,
            errors);
    }

    public async Task<LegacyOperationResult> ImportAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureImportServices();
        var analysis = await AnalyzeAsync(cancellationToken);
        var results = new List<TableImportResult>();
        var errors = analysis.Errors.ToList();
        var collisions = FindCollisions(analysis.Databases);

        foreach (var database in analysis.Databases)
        {
            foreach (var table in database.Tables)
            {
                if (collisions.Contains(table.Name))
                {
                    var error =
                        $"La tabla '{table.Name}' aparece en varios MDB del lote. " +
                        "No se mezclan datos automáticamente.";
                    results.Add(new(
                        database.FilePath,
                        table.Name,
                        ImportStatus.Conflict,
                        false,
                        table.RowCount,
                        0,
                        error));
                    errors.Add($"{database.FilePath}: {error}");
                    continue;
                }

                await ImportTableAsync(
                    database,
                    table,
                    results,
                    errors,
                    cancellationToken);
            }
        }

        return analysis with
        {
            IsAnalysis = false,
            Tables = results,
            Errors = errors
        };
    }

    private async Task ImportTableAsync(
        AccessDatabaseSchema database,
        AccessTableSchema table,
        ICollection<TableImportResult> results,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        long? historyId = null;
        var tableCreated = false;

        try
        {
            foreach (var column in table.Columns)
            {
                _typeMapper.Map(column);
            }

            var alreadyImported = await _historyService!.WasSuccessfullyImportedAsync(
                database.SourceHash,
                table.Name,
                cancellationToken);
            historyId = await _historyService.StartAsync(
                database,
                table,
                cancellationToken);

            if (ImportDeduplicationPolicy.ShouldSkip(
                    alreadyImported,
                    _options.ForceImport))
            {
                await _historyService.FinishAsync(
                    historyId.Value,
                    ImportStatus.SkippedAlreadyImported,
                    0,
                    null,
                    cancellationToken);
                results.Add(new(
                    database.FilePath,
                    table.Name,
                    ImportStatus.SkippedAlreadyImported,
                    false,
                    table.RowCount,
                    0,
                    null));
                return;
            }

            var exists = await _schemaService!.TableExistsAsync(
                table.Name,
                cancellationToken);
            if (exists)
            {
                var sqlTable = await _schemaService.ReadTableSchemaAsync(
                    table.Name,
                    cancellationToken);
                var differences = _comparisonService.Compare(table, sqlTable);
                var blocking = differences.Where(difference => difference.BlocksImport).ToArray();
                if (blocking.Length > 0)
                {
                    throw new SqlSchemaConflictException(
                        string.Join(
                            "; ",
                            blocking.Select(difference =>
                                $"{difference.Kind} {difference.Subject}: {difference.Message}")));
                }
            }
            else
            {
                _logger.LogInformation("[SQL] Creando dbo.{Table}", table.Name);
                await _schemaService.CreateTableAsync(table, cancellationToken);
                tableCreated = true;
                _logger.LogInformation("[SQL] Tabla creada");
            }

            var imported = await _bulkImporter!.ImportAsync(
                database,
                table,
                cancellationToken);
            await _historyService.FinishAsync(
                historyId.Value,
                ImportStatus.Success,
                imported,
                null,
                cancellationToken);

            results.Add(new(
                database.FilePath,
                table.Name,
                ImportStatus.Success,
                tableCreated,
                table.RowCount,
                imported,
                null));
            _logger.LogInformation(
                "[IMPORT] {Table}; Origen: {Source}; Importados: {Imported}; Resultado: OK",
                table.Name,
                table.RowCount,
                imported);
        }
        catch (Exception exception)
        {
            var error =
                $"Archivo: {database.FilePath}; Tabla: {table.Name}; " +
                $"Mensaje: {exception.Message}";
            errors.Add(error);
            if (historyId.HasValue)
            {
                await _historyService!.FinishAsync(
                    historyId.Value,
                    ImportStatus.Failed,
                    0,
                    exception.Message,
                    CancellationToken.None);
            }

            results.Add(new(
                database.FilePath,
                table.Name,
                ImportStatus.Failed,
                tableCreated,
                table.RowCount,
                0,
                exception.Message));
            _logger.LogError(exception, "[ERROR] {Error}", error);
        }
    }

    private static HashSet<string> FindCollisions(
        IReadOnlyList<AccessDatabaseSchema> databases)
    {
        return databases
            .SelectMany(database => database.Tables.Select(table => table.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureImportServices()
    {
        if (_schemaService is null ||
            _bulkImporter is null ||
            _historyService is null)
        {
            throw new InvalidOperationException(
                "Los servicios SQL no están configurados para importar.");
        }
    }
}
