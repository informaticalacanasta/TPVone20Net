using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Import;
using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Sources;

namespace TPVOne.LegacyAccessImporter.Import;

internal sealed class LegacyDatabaseImportService
{
    private readonly LegacyImportCoordinator _coordinator;
    private readonly ILogger<LegacyDatabaseImportService> _logger;

    public LegacyDatabaseImportService(
        LegacyImportOptions options,
        DaoToSqlTypeMapper typeMapper,
        SchemaComparisonService comparison,
        ILegacySqlSchemaPort? schemaService,
        ILegacyDataCopyPort? bulkImporter,
        ILegacyImportHistoryPort? historyService,
        ILogger<LegacyDatabaseImportService> logger)
    {
        _logger = logger;
        _coordinator = new LegacyImportCoordinator(
            new FileLegacyTableSourceScanner(options),
            typeMapper,
            comparison,
            schemaService,
            bulkImporter,
            historyService,
            options);
    }

    public async Task<PipelineResult> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _coordinator.AnalyzeAsync(cancellationToken);
        LogAnalysis(result);
        return result;
    }

    public async Task<PipelineResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var result = await _coordinator.ImportAsync(cancellationToken);
        foreach (var table in result.Tables)
        {
            _logger.LogInformation(
                "[IMPORT] Tabla: {Table}; Esquema: {Schema}; Datos: {Data}; Filas: {Rows}; Resultado: {Status}",
                table.TableName,
                table.SchemaStatus,
                table.DataStatus,
                table.ImportedRowCount,
                table.Status);
            if (table.Error is not null)
            {
                _logger.LogError("[ERROR] {Table}: {Error}", table.TableName, table.Error);
            }
        }

        return result;
    }

    private void LogAnalysis(PipelineResult result)
    {
        _logger.LogInformation(
            "[LEGACY] Definiciones TXT encontradas: {Count}",
            result.SchemaDefinitionsFound);
        _logger.LogInformation(
            "[SCHEMA] Tablas legacy válidas: {Count}",
            result.ValidLegacyTables);
        _logger.LogInformation(
            "[DATA] Tablas sin CSV: {Count}",
            result.TablesWithoutData);
        _logger.LogInformation(
            "[CSV] CSV asociados: {Count}",
            result.AssociatedDataSources);
        _logger.LogInformation(
            "[CSV] CSV huérfanos: {Count}",
            result.OrphanDataSources);
        _logger.LogInformation("[SCHEMA] Columnas: {Count}", result.Columns);
        _logger.LogInformation(
            "[DATA] Registros disponibles: {Count}",
            result.AvailableRecords);

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("[LEGACY] {Warning}", warning);
        }

        foreach (var error in result.Errors)
        {
            _logger.LogError("[ERROR] {Error}", error);
        }
    }
}
