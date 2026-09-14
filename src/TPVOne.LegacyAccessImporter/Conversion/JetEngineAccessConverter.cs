using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TPVOne.LegacyAccess.Core.Access;
using TPVOne.LegacyAccess.Core.Conversion;
using TPVOne.LegacyAccess.Core.Exceptions;

namespace TPVOne.LegacyAccessImporter.Conversion;

internal sealed class JetEngineAccessConverter : ILegacyAccessConverter
{
    private readonly AccessDatabaseReader _reader;
    private readonly ILogger<JetEngineAccessConverter> _logger;

    public JetEngineAccessConverter(
        AccessDatabaseReader reader,
        ILogger<JetEngineAccessConverter> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public void ConvertToJet4(string sourceFile, string destinationFile, string? password = null)
    {
        var jet4Temp = ConvertedPathResolver.GetJet4TemporaryPath(destinationFile);
        DeleteIfExists(jet4Temp);
        DeleteIfExists(destinationFile);

        try
        {
            var format = AccessFormatDetector.Detect(sourceFile);
            var jet4Source = sourceFile;
            if (AccessFormatDetector.RequiresJet4Conversion(format))
            {
                _logger.LogInformation(
                    "[CONVERT] Access 97 -> Jet 4 (motor) {File}",
                    Path.GetFileName(sourceFile));
                CompactWithJro(sourceFile, jet4Temp, password);
                jet4Source = jet4Temp;
            }

            var tables = _reader.ListUserTableNames(jet4Source, password);
            _logger.LogInformation(
                "[CONVERT] Jet 4 -> Access 2002-2003 (proyecto) {File} ({TableCount} tablas)",
                Path.GetFileName(sourceFile),
                tables.Count);

            StaRunner.Run(() =>
                Access2002ProjectBuilder.BuildFromJet4(jet4Source, destinationFile, tables));

            WaitUntilFileAvailable(destinationFile);
            var destinationFormat = AccessFormatDetector.Detect(destinationFile);
            if (AccessFormatDetector.RequiresJet4Conversion(destinationFormat))
            {
                throw new AccessConversionException(
                    $"El destino '{destinationFile}' sigue en formato {AccessFormatDetector.Describe(destinationFormat)}.");
            }

            if (!_reader.HasAccessApplicationCatalog(destinationFile, password))
            {
                throw new AccessConversionException(
                    $"El destino '{destinationFile}' es Jet 4 pero no tiene catálogo Access 2000-2003. " +
                    "Access seguirá pidiendo transformarlo.");
            }

            EnsureSameUserTables(
                tables,
                _reader.ListUserTableNames(destinationFile, password));
        }
        catch
        {
            DeleteIfExists(destinationFile);
            throw;
        }
        finally
        {
            DeleteIfExists(jet4Temp);
        }
    }

    private static void CompactWithJro(
        string sourceFile,
        string destinationFile,
        string? password)
    {
        var type = Type.GetTypeFromProgID("JRO.JetEngine")
            ?? throw new AccessProviderNotInstalledException(
                "JRO.JetEngine no está registrado en este proceso x86. " +
                "Instale Microsoft Jet 4.0 / Access Database Engine (x86) para convertir Access 97.");

        object? engine = null;
        try
        {
            engine = Activator.CreateInstance(type)
                ?? throw new AccessConversionException(
                    "No se pudo crear la instancia COM JRO.JetEngine.");

            type.InvokeMember(
                "CompactDatabase",
                BindingFlags.InvokeMethod,
                binder: null,
                target: engine,
                args:
                [
                    BuildJetConnectionString(sourceFile, password),
                    BuildJetConnectionString(destinationFile, password, engineType: 5)
                ]);
        }
        catch (Exception exception) when (exception is not LegacyAccessException)
        {
            throw new AccessConversionException(
                $"No se pudo convertir '{sourceFile}' a Jet 4. " +
                RootMessage(exception),
                exception);
        }
        finally
        {
            if (engine is not null)
            {
                Marshal.FinalReleaseComObject(engine);
            }
        }
    }

    private static void WaitUntilFileAvailable(string filePath)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                Thread.Sleep(200);
            }
        }

        throw new AccessConversionException(
            $"Access no liberó el archivo '{filePath}' tras convertirlo.",
            lastException);
    }

    private static void EnsureSameUserTables(
        IReadOnlyList<string> source,
        IReadOnlyList<string> destination)
    {
        var missing = source
            .Where(name => !name.StartsWith('~'))
            .Except(destination, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var extra = destination
            .Where(name => !name.StartsWith('~'))
            .Except(source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length == 0 && extra.Length == 0)
        {
            return;
        }

        throw new AccessConversionException(
            "Las tablas de usuario no coinciden tras convertir. " +
            $"Faltan: {FormatNames(missing)}. Extra: {FormatNames(extra)}.");
    }

    private static string FormatNames(IReadOnlyList<string> names)
    {
        return names.Count == 0 ? "(ninguna)" : string.Join(", ", names);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string BuildJetConnectionString(
        string filePath,
        string? password,
        int? engineType = null)
    {
        var builder = new System.Data.OleDb.OleDbConnectionStringBuilder
        {
            Provider = "Microsoft.Jet.OLEDB.4.0",
            DataSource = filePath
        };
        if (!string.IsNullOrEmpty(password))
        {
            builder["Jet OLEDB:Database Password"] = password;
        }

        if (engineType.HasValue)
        {
            builder["Jet OLEDB:Engine Type"] = engineType.Value;
        }

        return builder.ConnectionString;
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
