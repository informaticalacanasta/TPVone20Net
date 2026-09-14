using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess;

internal sealed class LegacyImporterProcess
{
    public const string ResultPrefix = "TPVONE_RESULT_JSON:";

    public async Task<PipelineResult> RunAsync(
        string mode,
        LegacyAccessImportOptions options,
        string sqlConnectionString,
        string? decisionsFile = null,
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
        startInfo.ArgumentList.Add("--converted");
        startInfo.ArgumentList.Add(options.ConvertedDirectory);
        startInfo.ArgumentList.Add("--batch-size");
        startInfo.ArgumentList.Add(options.BatchSize.ToString());
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add(options.CommandTimeoutSeconds.ToString());
        if (!string.IsNullOrWhiteSpace(decisionsFile))
        {
            startInfo.ArgumentList.Add("--decisions");
            startInfo.ArgumentList.Add(decisionsFile);
        }

        startInfo.Environment["TPVONE_SQL_CONNECTION"] = sqlConnectionString;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "No se pudo iniciar el importador Access x86.");

        string? resultJson = null;
        var stdoutTask = PumpAsync(
            process.StandardOutput,
            line =>
            {
                if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
                {
                    resultJson = line[ResultPrefix.Length..];
                    return;
                }

                Console.Out.WriteLine(line);
            },
            cancellationToken);
        var stderrTask = PumpAsync(
            process.StandardError,
            Console.Error.WriteLine,
            cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);

        PipelineResult? result = null;
        if (!string.IsNullOrWhiteSpace(resultJson))
        {
            result = JsonSerializer.Deserialize<PipelineResult>(
                resultJson,
                PipelineJson.Options);
        }

        if (process.ExitCode != 0 && process.ExitCode != 2)
        {
            throw new InvalidOperationException(
                $"El importador Access x86 finalizó con código {process.ExitCode}.");
        }

        return result ?? new PipelineResult(
            mode,
            null,
            [],
            [],
            [],
            [],
            ["El importador no devolvió un resultado JSON."]);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        Action<string> write,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                FlushLine(builder, write, final: true);
                return;
            }

            builder.Append(buffer.AsSpan(0, read));
            FlushLine(builder, write, final: false);
        }
    }

    private static void FlushLine(StringBuilder builder, Action<string> write, bool final)
    {
        while (true)
        {
            var text = builder.ToString();
            var index = text.IndexOfAny(['\r', '\n']);
            if (index < 0)
            {
                if (final && text.Length > 0)
                {
                    write(text);
                    builder.Clear();
                }

                return;
            }

            write(text[..index]);
            var skip = 1;
            if (text[index] == '\r' &&
                index + 1 < text.Length &&
                text[index + 1] == '\n')
            {
                skip = 2;
            }

            builder.Remove(0, index + skip);
        }
    }
}
