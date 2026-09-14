using TPVOne.LegacyAccess.Core.Data;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Planning;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Sources;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccess.Core.Import;

public sealed class LegacyImportCoordinator
{
    private readonly ILegacyTableSourceScanner _scanner;
    private readonly DaoToSqlTypeMapper _typeMapper;
    private readonly SchemaComparisonService _comparison;
    private readonly ILegacySqlSchemaPort? _sql;
    private readonly ILegacyDataCopyPort? _dataCopy;
    private readonly ILegacyImportHistoryPort? _history;
    private readonly LegacyImportOptions _options;

    public LegacyImportCoordinator(
        ILegacyTableSourceScanner scanner,
        DaoToSqlTypeMapper typeMapper,
        SchemaComparisonService comparison,
        ILegacySqlSchemaPort? sql,
        ILegacyDataCopyPort? dataCopy,
        ILegacyImportHistoryPort? history,
        LegacyImportOptions options)
    {
        _scanner = scanner;
        _typeMapper = typeMapper;
        _comparison = comparison;
        _sql = sql;
        _dataCopy = dataCopy;
        _history = history;
        _options = options;
    }

    public async Task<PipelineResult> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var discovery = _scanner.Discover(_options.SourceDirectory);
        var analysis = new List<LegacyAnalysisItem>();
        var warnings = discovery.Warnings.ToList();
        var errors = discovery.Errors.ToList();

        foreach (var source in discovery.Sources)
        {
            analysis.Add(await AnalyzeSourceAsync(source, errors, cancellationToken));
        }

        return new(
            "analyze",
            analysis,
            [],
            [],
            discovery.OrphanDataFiles,
            warnings,
            errors);
    }

    public async Task<PipelineResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        if (_sql is null || _dataCopy is null || _history is null)
        {
            throw new InvalidOperationException(
                "Los servicios SQL no están configurados para importar.");
        }

        var discovery = _scanner.Discover(_options.SourceDirectory);
        var analysis = new List<LegacyAnalysisItem>();
        var results = new List<TableImportResult>();
        var warnings = discovery.Warnings.ToList();
        var errors = discovery.Errors.ToList();
        var collisions = CollisionDetector.FindTableNameCollisions(
            discovery.Sources.Select(source => source.LogicalName));

        foreach (var source in discovery.Sources)
        {
            var item = await AnalyzeSourceAsync(source, errors, cancellationToken);
            analysis.Add(item);
            if (item.Error is not null)
            {
                results.Add(await RecordAsync(
                    source,
                    item,
                    SchemaStatus.Failed,
                    DataStatus.NotProcessed,
                    ImportStatus.Failed,
                    created: false,
                    imported: 0,
                    previousRows: null,
                    item.Error,
                    cancellationToken));
                continue;
            }

            if (collisions.Contains(source.LogicalName))
            {
                var error =
                    $"La tabla '{source.LogicalName}' aparece en más de un TXT. " +
                    "No se mezclan esquemas ni datos automáticamente.";
                errors.Add(error);
                results.Add(await RecordAsync(
                    source,
                    item,
                    SchemaStatus.Failed,
                    DataStatus.NotProcessed,
                    ImportStatus.Failed,
                    created: false,
                    imported: 0,
                    previousRows: null,
                    error,
                    cancellationToken));
                continue;
            }

            try
            {
                results.Add(await ImportSourceAsync(source, item, cancellationToken));
            }
            catch (Exception exception)
            {
                var error = RootMessage(exception);
                errors.Add($"{item.LogicalName}: {error}");
                results.Add(await RecordAsync(
                    source,
                    item,
                    SchemaStatus.Failed,
                    source.DataSource is null ? DataStatus.NotAvailable : DataStatus.Failed,
                    ImportStatus.Failed,
                    created: false,
                    imported: 0,
                    previousRows: null,
                    error,
                    CancellationToken.None));
            }
        }

        return new(
            "import",
            analysis,
            [],
            results,
            discovery.OrphanDataFiles,
            warnings,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<LegacyAnalysisItem> AnalyzeSourceAsync(
        LegacyTableSource source,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var schema = await source.SchemaSource.ReadSchemaAsync(cancellationToken);
            foreach (var column in schema.Columns)
            {
                _typeMapper.Map(column);
            }

            var structureHash = FileHashCalculator.CalculateSha256(source.SchemaSource.Location);
            string? dataHash = null;
            long records = 0;
            string? dataError = null;
            if (source.DataSource is not null)
            {
                dataHash = FileHashCalculator.CalculateSha256(source.DataSource.Location);
                try
                {
                    var header = source.DataSource.ReadHeader();
                    CsvHeaderValidator.EnsureMatches(header, schema);
                    using (var reader = source.DataSource.OpenReader(schema))
                    {
                        while (reader.Read())
                        {
                        }
                    }
                    records = source.DataSource.CountRecords();
                }
                catch (Exception exception)
                {
                    dataError = RootMessage(exception);
                    errors.Add($"{schema.Name}: {dataError}");
                }
            }

            return new(
                schema.Name,
                source.SchemaSource.Location,
                source.DataSource?.Location,
                structureHash,
                dataHash,
                schema.Columns.Count,
                schema.Indexes.Count,
                records,
                source.DataSource is not null,
                [],
                null,
                dataError);
        }
        catch (Exception exception)
        {
            var error = $"{source.LogicalName}: {RootMessage(exception)}";
            errors.Add(error);
            var structureHash = File.Exists(source.SchemaSource.Location)
                ? FileHashCalculator.CalculateSha256(source.SchemaSource.Location)
                : string.Empty;
            return new(
                source.LogicalName,
                source.SchemaSource.Location,
                source.DataSource?.Location,
                structureHash,
                null,
                0,
                0,
                0,
                source.DataSource is not null,
                [],
                RootMessage(exception));
        }
    }

    private async Task<TableImportResult> ImportSourceAsync(
        LegacyTableSource source,
        LegacyAnalysisItem analysis,
        CancellationToken cancellationToken)
    {
        var schema = await source.SchemaSource.ReadSchemaAsync(cancellationToken);
        foreach (var column in schema.Columns)
        {
            _typeMapper.Map(column);
        }

        var exists = await _sql!.TableExistsAsync(schema.Name, cancellationToken);
        long? previousRows = exists
            ? await _sql.CountRowsAsync(schema.Name, cancellationToken)
            : null;
        SchemaStatus schemaStatus;
        var created = false;

        if (!exists)
        {
            await _sql.CreateTableAsync(schema, cancellationToken);
            await _sql.CreateIndexesAsync(schema, cancellationToken);
            schemaStatus = SchemaStatus.Created;
            created = true;
        }
        else
        {
            var sqlSchema = await _sql.ReadTableSchemaAsync(schema.Name, cancellationToken);
            var differences = _comparison.Compare(schema, sqlSchema);
            if (differences.Any(difference => difference.BlocksImport))
            {
                var message = string.Join(
                    " ",
                    differences.Where(difference => difference.BlocksImport).Select(difference => difference.Message));
                return await RecordAsync(
                    source,
                    analysis,
                    SchemaStatus.Conflict,
                    DataStatus.NotProcessed,
                    ImportStatus.Conflict,
                    created: false,
                    imported: 0,
                    previousRows,
                    message,
                    cancellationToken);
            }

            await _sql.CreateIndexesAsync(schema, cancellationToken);
            schemaStatus = differences.Any(difference =>
                difference.Kind is SchemaDifferenceKind.ExactMatch)
                ? SchemaStatus.AlreadyExists
                : SchemaStatus.Compatible;
        }

        if (source.DataSource is null)
        {
            return await RecordAsync(
                source,
                analysis with { AvailableRecords = 0 },
                schemaStatus,
                DataStatus.NotAvailable,
                ImportStatus.Success,
                created,
                imported: 0,
                previousRows,
                error: null,
                cancellationToken);
        }

        if (analysis.DataError is not null)
        {
            return await RecordAsync(
                source,
                analysis,
                schemaStatus,
                DataStatus.Failed,
                ImportStatus.Failed,
                created,
                imported: 0,
                previousRows,
                analysis.DataError,
                cancellationToken);
        }

        var header = source.DataSource.ReadHeader();
        try
        {
            CsvHeaderValidator.EnsureMatches(header, schema);
        }
        catch (CsvHeaderMismatchException exception)
        {
            return await RecordAsync(
                source,
                analysis,
                schemaStatus,
                DataStatus.Failed,
                ImportStatus.Failed,
                created,
                imported: 0,
                previousRows,
                exception.Message,
                cancellationToken);
        }

        var dataHash = analysis.DataHash
            ?? FileHashCalculator.CalculateSha256(source.DataSource.Location);
        if (!_options.ForceImport &&
            await _history!.WasDataImportedAsync(schema.Name, dataHash, cancellationToken))
        {
            return await RecordAsync(
                source,
                analysis,
                schemaStatus,
                DataStatus.AlreadyImported,
                ImportStatus.SkippedAlreadyImported,
                created,
                imported: 0,
                previousRows,
                error: null,
                cancellationToken);
        }

        var currentRows = await _sql.CountRowsAsync(schema.Name, cancellationToken);
        if (currentRows != 0)
        {
            return await RecordAsync(
                source,
                analysis,
                schemaStatus,
                DataStatus.Failed,
                ImportStatus.Failed,
                created,
                imported: 0,
                previousRows,
                $"La tabla '{schema.Name}' no está vacía. No se truncará ni se duplicarán filas.",
                cancellationToken);
        }

        try
        {
            var expected = source.DataSource.CountRecords();
            var imported = await _dataCopy!.CopyAsync(
                schema with { RowCount = expected },
                source.DataSource,
                schema.Name,
                expected,
                cancellationToken);
            return await RecordAsync(
                source,
                analysis with { AvailableRecords = expected },
                schemaStatus,
                DataStatus.Imported,
                ImportStatus.Success,
                created,
                imported,
                previousRows,
                error: null,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return await RecordAsync(
                source,
                analysis,
                schemaStatus,
                DataStatus.Failed,
                ImportStatus.Failed,
                created,
                imported: 0,
                previousRows,
                RootMessage(exception),
                CancellationToken.None);
        }
    }

    private async Task<TableImportResult> RecordAsync(
        LegacyTableSource source,
        LegacyAnalysisItem analysis,
        SchemaStatus schemaStatus,
        DataStatus dataStatus,
        ImportStatus status,
        bool created,
        long imported,
        long? previousRows,
        string? error,
        CancellationToken cancellationToken)
    {
        var result = new TableImportResult(
            analysis.LogicalName,
            source.SchemaSource.Location,
            source.DataSource?.Location,
            analysis.StructureHash,
            analysis.DataHash,
            schemaStatus,
            dataStatus,
            status,
            created,
            analysis.AvailableRecords,
            imported,
            previousRows,
            error);

        if (_history is null)
        {
            return result;
        }

        var id = await _history.RecordStartAsync(result, cancellationToken);
        await _history.RecordFinishAsync(id, result, cancellationToken);
        return result;
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
