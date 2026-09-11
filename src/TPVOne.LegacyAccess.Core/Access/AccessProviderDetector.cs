using System.Data.OleDb;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Access;

public sealed class AccessProviderDetector
{
    private static readonly string[] Providers =
    [
        "Microsoft.ACE.OLEDB.16.0",
        "Microsoft.ACE.OLEDB.12.0",
        "Microsoft.Jet.OLEDB.4.0"
    ];

    public AccessProviderSelection Detect(string filePath, string? password = null)
    {
        var attempts = new List<ProviderAttempt>();
        Exception? lastException = null;

        foreach (var provider in Providers)
        {
            var connectionString = BuildConnectionString(provider, filePath, password);
            try
            {
                using var connection = new OleDbConnection(connectionString);
                connection.Open();
                attempts.Add(new(provider, "Disponible", null));
                return new(provider, connectionString, attempts);
            }
            catch (Exception exception) when (
                exception is OleDbException or InvalidOperationException)
            {
                lastException = exception;
                attempts.Add(new(
                    provider,
                    IsProviderMissing(exception)
                        ? "No instalado para x86"
                        : ClassifyFailure(exception),
                    exception.Message));
            }
        }

        var diagnostic = string.Join(
            Environment.NewLine,
            attempts.Select(attempt =>
                $"{attempt.Provider}: {attempt.Status} ({attempt.Detail})"));
        var message =
            $"No se ha podido abrir el archivo '{filePath}'.{Environment.NewLine}" +
            $"Proveedores probados:{Environment.NewLine}{diagnostic}{Environment.NewLine}" +
            "Instale Microsoft Access Database Engine x86 o use un MDB válido y sin contraseña.";

        if (attempts.All(attempt => attempt.Status == "No instalado para x86"))
        {
            throw new AccessProviderNotInstalledException(message, lastException);
        }

        if (attempts.Any(attempt => attempt.Status == "Protegido con contraseña"))
        {
            throw new AccessDatabasePasswordProtectedException(message, lastException);
        }

        if (attempts.Any(attempt => attempt.Status == "Archivo dañado"))
        {
            throw new AccessDatabaseCorruptedException(message, lastException);
        }

        throw new UnsupportedAccessVersionException(message, lastException);
    }

    public static string BuildConnectionString(
        string provider,
        string filePath,
        string? password = null)
    {
        var builder = new OleDbConnectionStringBuilder
        {
            Provider = provider,
            DataSource = filePath
        };

        builder["Persist Security Info"] = false;
        if (!string.IsNullOrEmpty(password))
        {
            builder["Jet OLEDB:Database Password"] = password;
        }

        return builder.ConnectionString;
    }

    private static bool IsProviderMissing(Exception exception)
    {
        return exception.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("no está registrado", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("class not registered", StringComparison.OrdinalIgnoreCase) ||
            exception.HResult == unchecked((int)0x80040154);
    }

    private static string ClassifyFailure(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("contraseña", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not a valid account", StringComparison.OrdinalIgnoreCase))
        {
            return "Protegido con contraseña";
        }

        if (message.Contains("corrupt", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("dañad", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("repair", StringComparison.OrdinalIgnoreCase))
        {
            return "Archivo dañado";
        }

        return "Formato no reconocido o no compatible";
    }
}
