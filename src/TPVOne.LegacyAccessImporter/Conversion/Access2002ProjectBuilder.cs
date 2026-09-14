using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using TPVOne.LegacyAccess.Core.Exceptions;

namespace TPVOne.LegacyAccessImporter.Conversion;

internal static class Access2002ProjectBuilder
{
    private const int AcImport = 0;
    private const int AcTable = 0;
    private const int AcFileFormatAccess2002 = 10;
    private const int AcQuitSaveNone = 2;
    private const int MsoAutomationSecurityForceDisable = 3;

    public static void BuildFromJet4(
        string jet4File,
        string destinationFile,
        IReadOnlyList<string> userTables)
    {
        var accessType = Type.GetTypeFromProgID("Access.Application")
            ?? Type.GetTypeFromProgID("Access.Application.15")
            ?? throw new AccessProviderNotInstalledException(
                "No se encontró Microsoft Access (MSACCESS.EXE). " +
                "JRO solo actualiza el motor Jet; sin Access no se puede crear el " +
                "proyecto 2002-2003 y el programa seguirá pidiendo transformar el MDB.");

        object? app = null;
        try
        {
            app = Activator.CreateInstance(accessType)
                ?? throw new AccessConversionException(
                    "No se pudo crear la instancia COM Access.Application.");

            TrySet(accessType, app, "Visible", false);
            TrySet(accessType, app, "UserControl", false);
            TrySet(accessType, app, "AutomationSecurity", MsoAutomationSecurityForceDisable);
            TryCall(accessType, app, "SetOption", "Default File Format", AcFileFormatAccess2002);

            var destination = Path.GetFullPath(destinationFile);
            CreateAccess2002Database(accessType, app, destination);

            var doCmd = accessType.InvokeMember(
                "DoCmd",
                BindingFlags.GetProperty,
                binder: null,
                target: app,
                args: null)
                ?? throw new AccessConversionException("Access no expuso DoCmd.");

            dynamic command = doCmd;
            TrySet(() => command.SetWarnings(false));

            var source = Path.GetFullPath(jet4File);
            foreach (var table in userTables.Where(name => !name.StartsWith('~')))
            {
                try
                {
                    TransferTable(command, source, table);
                }
                catch (Exception exception)
                {
                    throw new AccessConversionException(
                        $"No se pudo copiar la tabla '{table}' al MDB Access 2002-2003. " +
                        RootMessage(exception),
                        exception);
                }
            }

            TryCall(accessType, app, "CloseCurrentDatabase");
        }
        catch (Exception exception) when (exception is not LegacyAccessException)
        {
            throw new AccessConversionException(
                $"No se pudo crear el proyecto Access 2002-2003 en '{destinationFile}'. " +
                RootMessage(exception),
                exception);
        }
        finally
        {
            if (app is not null)
            {
                try
                {
                    accessType.InvokeMember(
                        "Quit",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: app,
                        args: [AcQuitSaveNone]);
                }
                catch
                {
                    TryCall(accessType, app, "Quit");
                }

                Marshal.FinalReleaseComObject(app);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static void CreateAccess2002Database(Type accessType, object app, string destination)
    {
        var version = ReadMajorVersion(accessType, app);
        var attempts = version >= 12
            ? new object[][]
            {
                [destination, AcFileFormatAccess2002, Missing.Value, Missing.Value, Missing.Value],
                [destination, AcFileFormatAccess2002],
                [destination]
            }
            : new object[][]
            {
                [destination]
            };

        Exception? lastException = null;
        foreach (var args in attempts)
        {
            try
            {
                accessType.InvokeMember(
                    "NewCurrentDatabase",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    target: app,
                    args: args);
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        throw new AccessConversionException(
            $"NewCurrentDatabase no aceptó la firma de Access {version:0.0} para '{destination}'.",
            lastException);
    }

    private static void TransferTable(dynamic doCmd, string sourceFile, string tableName)
    {
        Exception? lastException = null;
        try
        {
            doCmd.TransferDatabase(
                AcImport,
                "Microsoft Access",
                sourceFile,
                AcTable,
                tableName,
                tableName,
                false,
                Missing.Value);
            return;
        }
        catch (Exception exception)
        {
            lastException = exception;
        }

        try
        {
            doCmd.TransferDatabase(
                AcImport,
                "Microsoft Access",
                sourceFile,
                AcTable,
                tableName,
                tableName,
                false);
            return;
        }
        catch (Exception exception)
        {
            lastException = exception;
        }

        try
        {
            doCmd.TransferDatabase(
                AcImport,
                "Microsoft Access",
                sourceFile,
                AcTable,
                tableName,
                tableName);
            return;
        }
        catch (Exception exception)
        {
            lastException = exception;
        }

        throw lastException
            ?? new AccessConversionException($"No se pudo transferir '{tableName}'.");
    }

    private static double ReadMajorVersion(Type accessType, object app)
    {
        try
        {
            var version = Convert.ToString(
                accessType.InvokeMember(
                    "Version",
                    BindingFlags.GetProperty,
                    binder: null,
                    target: app,
                    args: null),
                CultureInfo.InvariantCulture);
            if (double.TryParse(
                    version,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // Access 2003 a veces no expone Version de forma uniforme.
        }

        return 15;
    }

    private static void TrySet(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Propiedad u opción no disponible en esta versión de Access.
        }
    }

    private static void TrySet(Type type, object target, string name, object value)
    {
        try
        {
            type.InvokeMember(
                name,
                BindingFlags.SetProperty,
                binder: null,
                target: target,
                args: [value]);
        }
        catch
        {
            // Propiedad no disponible en esta versión de Access.
        }
    }

    private static void TryCall(Type type, object target, string name, params object[] args)
    {
        try
        {
            type.InvokeMember(
                name,
                BindingFlags.InvokeMethod,
                binder: null,
                target: target,
                args: args);
        }
        catch
        {
            // Método opcional no disponible en esta versión de Access.
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
