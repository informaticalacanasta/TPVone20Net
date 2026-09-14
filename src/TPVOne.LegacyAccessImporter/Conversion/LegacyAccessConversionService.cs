using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Conversion;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.LegacyAccessImporter.Conversion;

internal sealed class LegacyAccessConversionService
{
    private readonly LegacyAccessImportOptions _options;
    private readonly ILegacyAccessConverter _converter;
    private readonly AccessDatabaseReader _accessReader;
    private readonly ConversionHistoryService _history;
    private readonly ILogger<LegacyAccessConversionService> _logger;

    public LegacyAccessConversionService(
        LegacyAccessImportOptions options,
        ILegacyAccessConverter converter,
        AccessDatabaseReader accessReader,
        ConversionHistoryService history,
        ILogger<LegacyAccessConversionService> logger)
    {
        _options = options;
        _converter = converter;
        _accessReader = accessReader;
        _history = history;
        _logger = logger;
    }

    public async Task<ConversionBatchResult> ConvertAllAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.ConvertedDirectory);
        var files = LegacyFileScanner.FindMdbFiles(_options.SourceDirectory);
        var items = new List<ConversionItemResult>();
        var errors = new List<string>();

        foreach (var sourceFile in files)
        {
            try
            {
                items.Add(await ConvertOneAsync(sourceFile, cancellationToken));
            }
            catch (Exception exception)
            {
                var message =
                    $"Archivo: {sourceFile}; Operación: conversión Access; " +
                    $"Mensaje: {RootMessage(exception)}";
                errors.Add(message);
                _logger.LogError(exception, "[CONVERT] {Message}", message);
                items.Add(new(
                    sourceFile,
                    null,
                    string.Empty,
                    AccessJetFormat.Unknown,
                    ConversionAction.ConvertToJet4,
                    "Failed",
                    message));
            }
        }

        return new(
            files.Count,
            items.Count(item => item.Status == "Success"),
            items.Count(item => item.Status == "Copied"),
            items.Count(item => item.Status == "Skipped"),
            errors.Count,
            items,
            errors);
    }

    private async Task<ConversionItemResult> ConvertOneAsync(
        string sourceFile,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[SCAN] {File}", sourceFile);
        var sourceHash = await FileHashCalculator.CalculateSha256Async(
            sourceFile,
            cancellationToken);
        _logger.LogInformation("[HASH] SHA256={Hash}", sourceHash);

        var format = AccessFormatDetector.Detect(sourceFile);
        var destination = ConvertedPathResolver.GetConvertedPath(
            _options.SourceDirectory,
            sourceFile,
            _options.ConvertedDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var destinationExists = File.Exists(destination);
        var destinationIsComplete = destinationExists &&
            IsCompleteAccess2000Project(destination);
        var historyHash = destinationExists
            ? await _history.GetSuccessfulSourceHashAsync(destination, cancellationToken)
            : null;
        if (destinationExists &&
            await _history.WasSuccessfullyConvertedAsync(
                sourceHash,
                destination,
                cancellationToken))
        {
            historyHash = sourceHash;
        }

        var action = ConversionDeduplicationPolicy.Decide(
            format,
            sourceHash,
            destinationExists,
            historyHash,
            destinationIsComplete);
        if (action == ConversionAction.CopyCompatible &&
            SourceNeedsProjectUpgrade(format, sourceFile))
        {
            action = ConversionAction.ConvertToJet4;
        }

        var sourceInfo = new FileInfo(sourceFile);
        if (action == ConversionAction.SkipAlreadyConverted)
        {
            _logger.LogInformation(
                "[CONVERT] {File} ya convertido con mismo hash. OMITIDO.",
                Path.GetFileName(sourceFile));
            var skipped = new ConversionItemResult(
                sourceFile,
                destination,
                sourceHash,
                format,
                action,
                "Skipped",
                "Ya convertido anteriormente. Hash original coincidente.");
            await _history.RecordAsync(skipped, sourceInfo, new FileInfo(destination), cancellationToken);
            return skipped;
        }

        var tempPath = ConvertedPathResolver.GetTemporaryPath(destination);
        DeleteIfExists(tempPath);

        _logger.LogInformation(
            "[CONVERT] {File} Origen: {Format}; Acción: {Action}",
            Path.GetFileName(sourceFile),
            AccessFormatDetector.Describe(format),
            action);

        try
        {
            var canCopySource = !SourceNeedsProjectUpgrade(format, sourceFile);
            if (action == ConversionAction.CopyCompatible ||
                (action == ConversionAction.Reconvert && canCopySource))
            {
                File.Copy(sourceFile, tempPath, overwrite: true);
            }
            else
            {
                _converter.ConvertToJet4(sourceFile, tempPath);
            }

            ValidateConverted(tempPath);
            ReplaceAtomically(tempPath, destination);

            var status = action == ConversionAction.CopyCompatible ||
                (action == ConversionAction.Reconvert && canCopySource)
                ? "Copied"
                : "Success";
            var result = new ConversionItemResult(
                sourceFile,
                destination,
                sourceHash,
                format,
                action,
                status,
                $"Origen: {AccessFormatDetector.Describe(format)}. Destino: {destination}");
            _logger.LogInformation("[CONVERT] OK {File}", Path.GetFileName(sourceFile));
            await _history.RecordAsync(result, sourceInfo, new FileInfo(destination), cancellationToken);
            return result;
        }
        catch
        {
            DeleteIfExists(tempPath);
            throw;
        }
    }

    private bool SourceNeedsProjectUpgrade(AccessJetFormat format, string sourceFile)
    {
        return AccessFormatDetector.RequiresJet4Conversion(format) ||
            !_accessReader.HasAccessApplicationCatalog(sourceFile);
    }

    private bool IsCompleteAccess2000Project(string filePath)
    {
        try
        {
            var destinationFormat = AccessFormatDetector.Detect(filePath);
            return !AccessFormatDetector.RequiresJet4Conversion(destinationFormat) &&
                _accessReader.HasAccessApplicationCatalog(filePath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[CONVERT] No se pudo validar el catálogo Access de {File}. Se reconvertirá.",
                filePath);
            return false;
        }
    }

    private void ValidateConverted(string convertedFile)
    {
        var convertedFormat = AccessFormatDetector.Detect(convertedFile);
        if (AccessFormatDetector.RequiresJet4Conversion(convertedFormat))
        {
            throw new AccessConversionException(
                $"El archivo convertido '{convertedFile}' sigue en formato " +
                $"{AccessFormatDetector.Describe(convertedFormat)}.");
        }

        if (!_accessReader.HasAccessApplicationCatalog(convertedFile))
        {
            throw new AccessConversionException(
                $"El archivo convertido '{convertedFile}' es Jet 4 pero no tiene " +
                "catálogo Access 2000-2003. Access seguirá pidiendo transformarlo.");
        }

        var tables = _accessReader.ListUserTableNames(convertedFile);
        if (tables.Count == 0)
        {
            _logger.LogInformation(
                "[CONVERT] {File} no contiene tablas de usuario, pero es un proyecto Access 2000-2003.",
                convertedFile);
        }
    }

    private static void ReplaceAtomically(string temporaryFile, string destinationFile)
    {
        if (File.Exists(destinationFile))
        {
            var backup = destinationFile + ".bak";
            DeleteIfExists(backup);
            File.Replace(temporaryFile, destinationFile, backup);
            DeleteIfExists(backup);
        }
        else
        {
            File.Move(temporaryFile, destinationFile);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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
