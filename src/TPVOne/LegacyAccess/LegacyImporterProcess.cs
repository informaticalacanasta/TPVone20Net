using System.Diagnostics;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess;

internal sealed class LegacyImporterProcess
{
    public async Task<int> RunAsync(
        string mode,
        LegacyAccessImportOptions options,
        string? sqlConnectionString,
        CancellationToken cancellationToken = default)
    {
        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "LegacyAccessImporter",
            "TPVOne.LegacyAccessImporter.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "No se encontró el importador Access x86. Compile la solución completa.",
                executable);
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add(options.SourceDirectory);
        startInfo.ArgumentList.Add("--batch-size");
        startInfo.ArgumentList.Add(options.BatchSize.ToString());
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add(options.CommandTimeoutSeconds.ToString());
        startInfo.ArgumentList.Add("--force");
        startInfo.ArgumentList.Add(options.ForceImport.ToString());

        if (sqlConnectionString is not null)
        {
            startInfo.Environment["TPVONE_SQL_CONNECTION"] = sqlConnectionString;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "No se pudo iniciar el importador Access x86.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await standardOutput;
        var error = await standardError;
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (var line in output.Split(
                         Environment.NewLine,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith(
                        "TPVONE_RESULT_JSON:",
                        StringComparison.Ordinal))
                {
                    Console.Out.WriteLine(line);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.Write(error);
        }

        return process.ExitCode;
    }
}
