using TPVOne.LegacyAccess.Core.Classification;
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
    private readonly ILegacyImportInteraction _interaction;
    private readonly IBinaryColumnClassifier _binaryClassifier;

    public LegacyImportCoordinator(
        ILegacyTableSourceScanner scanner,
        DaoToSqlTypeMapper typeMapper,
        SchemaComparisonService comparison,
        ILegacySqlSchemaPort? sql,
        ILegacyDataCopyPort? dataCopy,
        ILegacyImportHistoryPort? history,
        LegacyImportOptions options,
        ILegacyImportInteraction? interaction = null,
        IBinaryColumnClassifier? binaryClassifier = null)
    {
        _scanner = scanner;
        _typeMapper = typeMapper;
        _comparison = comparison;
        _sql = sql;
        _dataCopy = dataCopy;
        _history = history;
        _options = options;
        _interaction = interaction ?? NullLegacyImportInteraction.Instance;
        _binaryClassifier = binaryClassifier ?? new BinaryColumnClassifier();
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
        _interaction.Inform($"Analizando {analysis.LogicalName}...");

        var schema = await source.SchemaSource.ReadSchemaAsync(cancellationToken);
        foreach (var column in schema.Columns)
        {
            _typeMapper.Map(column);
        }

        if (ProtectedInfrastructureTables.Contains(schema.Name))
        {
            var error =
                $"La tabla '{schema.Name}' es de infraestructura interna y no puede " +
                "crearse ni sobrescribirse desde archivos TXT/CSV.";
            return await RecordAsync(
                source,
                analysis,
                SchemaStatus.Failed,
                DataStatus.NotProcessed,
                ImportStatus.Failed,
                created: false,
                imported: 0,
                previousRows: null,
                error,
                cancellationToken);
        }

        var exists = await _sql!.TableExistsAsync(schema.Name, cancellationToken);
        long? previousRows = exists
            ? await _sql.CountRowsAsync(schema.Name, cancellationToken)
            : null;

        var dataError = analysis.DataError;
        if (source.DataSource is not null && dataError is null)
        {
            try
            {
                CsvHeaderValidator.EnsureMatches(source.DataSource.ReadHeader(), schema);
            }
            catch (CsvHeaderMismatchException exception)
            {
                dataError = exception.Message;
            }
        }

        var effectiveSchema = schema;
        if (source.DataSource is not null && dataError is null)
        {
            var kinds = _binaryClassifier.Classify(schema, source.DataSource);
            InformBinaryClassification(schema.Name, schema, kinds);
            effectiveSchema = EffectiveLegacySchema.Apply(schema, kinds);
        }

        if (dataError is not null)
        {
            return await RecordAsync(
                source,
                analysis,
                exists ? SchemaStatus.AlreadyExists : SchemaStatus.Failed,
                DataStatus.Failed,
                ImportStatus.Failed,
                created: false,
                imported: 0,
                previousRows,
                dataError,
                cancellationToken);
        }

        if (source.DataSource is null)
        {
            if (!exists)
            {
                _interaction.Inform($"La tabla '{schema.Name}' no existe. Creando...");
                await _sql.CreateTableAsync(effectiveSchema, cancellationToken);
                await _sql.CreateIndexesAsync(effectiveSchema, cancellationToken);
                return await RecordAsync(
                    source,
                    analysis with { AvailableRecords = 0 },
                    SchemaStatus.Created,
                    DataStatus.NotAvailable,
                    ImportStatus.Success,
                    created: true,
                    imported: 0,
                    previousRows,
                    error: null,
                    cancellationToken);
            }

            return await HandleExistingSchemaOnlyAsync(
                source,
                analysis,
                schema,
                previousRows,
                cancellationToken);
        }

        if (exists &&
            analysis.DataHash is not null &&
            ImportDeduplicationPolicy.ShouldSkipAlreadyImported(
                await _history!.WasDataImportedAsync(
                    schema.Name,
                    analysis.DataHash,
                    cancellationToken),
                _options.ForceImport))
        {
            _interaction.Inform(
                $"La tabla '{schema.Name}' ya fue importada previamente desde un origen " +
                "con el mismo contenido. Se omite.");
            return await RecordAsync(
                source,
                analysis,
                SchemaStatus.AlreadyExists,
                DataStatus.AlreadyImported,
                ImportStatus.SkippedAlreadyImported,
                created: false,
                imported: 0,
                previousRows,
                error: null,
                cancellationToken);
        }

        if (exists && !_interaction.ConfirmOverwrite(schema.Name))
        {
            _interaction.Inform(OverwriteConfirmationProtocol.Conserved(schema.Name));
            return await RecordAsync(
                source,
                analysis,
                SchemaStatus.AlreadyExists,
                DataStatus.NotProcessed,
                ImportStatus.SkippedByUser,
                created: false,
                imported: 0,
                previousRows,
                error: null,
                cancellationToken);
        }

        return await ImportViaStagingAsync(
            source,
            analysis,
            effectiveSchema,
            exists,
            previousRows,
            cancellationToken);
    }

    private async Task<TableImportResult> ImportViaStagingAsync(
        LegacyTableSource source,
        LegacyAnalysisItem analysis,
        LegacyTableSchema schema,
        bool destinationExisted,
        long? previousRows,
        CancellationToken cancellationToken)
    {
        var dataSource = source.DataSource
            ?? throw new InvalidOperationException(
                $"No hay CSV para importar '{schema.Name}'.");
        var expected = dataSource.CountRecords();
        var token = LegacyStagingNames.NewToken();
        var stagingName = LegacyStagingNames.StagingTable(schema.Name, token);
        var backupName = LegacyStagingNames.BackupTable(schema.Name, token);
        var schemaWithRows = schema with { RowCount = expected };
        _interaction.Inform(
            destinationExisted
                ? $"Importando '{schema.Name}'..."
                : $"La tabla '{schema.Name}' no existe. Creando...");

        var operations = new SqlReplacementOperations(
            _sql!,
            _dataCopy!,
            dataSource,
            schemaWithRows,
            expected,
            token,
            _interaction);

        try
        {
            var imported = await new SafeReplacementWorkflow().ReplaceOrCreateAsync(
                operations,
                schemaWithRows,
                schema.Name,
                stagingName,
                backupName,
                cancellationToken);
            return await RecordAsync(
                source,
                analysis with { AvailableRecords = expected },
                destinationExisted ? SchemaStatus.Replaced : SchemaStatus.Created,
                DataStatus.Imported,
                ImportStatus.Success,
                created: true,
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
                destinationExisted ? SchemaStatus.AlreadyExists : SchemaStatus.Failed,
                DataStatus.Failed,
                ImportStatus.Failed,
                created: false,
                imported: 0,
                previousRows,
                exception.Message,
                CancellationToken.None);
        }
    }

    private void InformBinaryClassification(
        string tableName,
        LegacyTableSchema schema,
        IReadOnlyDictionary<string, LegacyBinaryColumnKind> kinds)
    {
        foreach (var column in schema.Columns)
        {
            if (!kinds.TryGetValue(column.Name, out var kind))
            {
                continue;
            }

            if (kind == LegacyBinaryColumnKind.Utf16Text)
            {
                _interaction.Inform(
                    $"{tableName}.{column.Name}: binario legacy detectado como texto UTF-16LE → nvarchar(max)");
            }
            else
            {
                var sqlType = _typeMapper.Map(
                    kind == LegacyBinaryColumnKind.Binary
                    && column.SourceTypeName.Trim().ToUpperInvariant() == "DBMEMO"
                        ? column with { SourceTypeName = "dbLongBinary" }
                        : column).ToSql();
                _interaction.Inform(
                    $"{tableName}.{column.Name}: binario real → {sqlType}");
            }
        }
    }

    private async Task<TableImportResult> HandleExistingSchemaOnlyAsync(
        LegacyTableSource source,
        LegacyAnalysisItem analysis,
        LegacyTableSchema schema,
        long? previousRows,
        CancellationToken cancellationToken)
    {
        var sqlSchema = await _sql!.ReadTableSchemaAsync(schema.Name, cancellationToken);
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
        var schemaStatus = differences.Any(difference =>
            difference.Kind is SchemaDifferenceKind.ExactMatch)
            ? SchemaStatus.AlreadyExists
            : SchemaStatus.Compatible;
        return await RecordAsync(
            source,
            analysis with { AvailableRecords = 0 },
            schemaStatus,
            DataStatus.NotAvailable,
            ImportStatus.Success,
            created: false,
            imported: 0,
            previousRows,
            error: null,
            cancellationToken);
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

    private sealed class SqlReplacementOperations : IReplacementOperations
    {
        private readonly ILegacySqlSchemaPort _sql;
        private readonly ILegacyDataCopyPort _copy;
        private readonly ILegacyDataSource _dataSource;
        private readonly LegacyTableSchema _schema;
        private readonly long _expectedRows;
        private readonly string _indexSuffix;
        private readonly ILegacyImportInteraction _interaction;

        public SqlReplacementOperations(
            ILegacySqlSchemaPort sql,
            ILegacyDataCopyPort copy,
            ILegacyDataSource dataSource,
            LegacyTableSchema schema,
            long expectedRows,
            string indexSuffix,
            ILegacyImportInteraction interaction)
        {
            _sql = sql;
            _copy = copy;
            _dataSource = dataSource;
            _schema = schema;
            _expectedRows = expectedRows;
            _indexSuffix = indexSuffix;
            _interaction = interaction;
        }

        public Task CreateEmptyTableAsync(
            string tableName,
            LegacyTableSchema schema,
            CancellationToken cancellationToken)
        {
            return _sql.CreateTableAsync(schema, tableName, cancellationToken);
        }

        public async Task<long> CopyDataAsync(string tableName, CancellationToken cancellationToken)
        {
            _interaction.Inform($"Importando {Path.GetFileName(_dataSource.Location)}...");
            var progress = new Progress<LegacyCopyProgress>(update =>
                _interaction.Inform($"{update.RowsCopied} / {update.ExpectedRows}"));
            try
            {
                var copied = await _copy.CopyAsync(
                    _schema,
                    _dataSource,
                    tableName,
                    _expectedRows,
                    progress,
                    cancellationToken);
                _interaction.Inform($"{copied.RowsCopied} registros importados.");
                return copied.RowsCopied;
            }
            catch (Exception exception) when (exception is not DataImportException
                and not OperationCanceledException)
            {
                throw new DataImportException(
                    $"Error importando datos en staging de '{_schema.Name}'.",
                    exception);
            }
        }

        public async Task CreateIndexesAsync(
            string tableName,
            LegacyTableSchema schema,
            CancellationToken cancellationToken)
        {
            _interaction.Inform("Validando...");
            _interaction.Inform("Creando índices...");
            try
            {
                await _sql.CreateIndexesAsync(
                    schema,
                    tableName,
                    _indexSuffix,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not DataImportException)
            {
                throw new DataImportException(
                    $"Error creando índices de '{schema.Name}'.",
                    exception);
            }
        }

        public Task<long> CountAsync(string tableName, CancellationToken cancellationToken)
        {
            return _sql.CountRowsAsync(tableName, cancellationToken);
        }

        public Task<bool> ExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            return _sql.TableExistsAsync(tableName, cancellationToken);
        }

        public async Task SwapAtomicAsync(
            string destinationTableName,
            string stagingTableName,
            string? backupTableName,
            CancellationToken cancellationToken)
        {
            _interaction.Inform($"Sustituyendo tabla '{destinationTableName}'...");
            try
            {
                await _sql.SwapAtomicAsync(
                    destinationTableName,
                    stagingTableName,
                    backupTableName,
                    _schema.Indexes,
                    _indexSuffix,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not DataImportException)
            {
                throw new DataImportException(
                    $"Error sustituyendo tabla '{destinationTableName}'.",
                    exception);
            }

            _interaction.Inform("Importación completada.");
        }

        public Task DropIfExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            return _sql.DropTableAsync(tableName, cancellationToken);
        }
    }

    private sealed class NullLegacyImportInteraction : ILegacyImportInteraction
    {
        public static readonly NullLegacyImportInteraction Instance = new();

        public void Inform(string message)
        {
        }

        public bool ConfirmOverwrite(string tableName)
        {
            throw new InvalidOperationException(
                $"Se pidió confirmar la sobrescritura de '{tableName}' sin interacción configurada.");
        }
    }
}
