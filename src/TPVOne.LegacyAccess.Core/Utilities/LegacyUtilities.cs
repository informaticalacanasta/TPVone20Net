using System.Security.Cryptography;

namespace TPVOne.LegacyAccess.Core.Utilities;

public static class SqlIdentifier
{
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}

public static class AccessObjectFilter
{
    public static bool IsUserTable(string name, string? tableType = "TABLE")
    {
        return string.Equals(tableType, "TABLE", StringComparison.OrdinalIgnoreCase) &&
            !name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase);
    }
}

public static class LegacyFileScanner
{
    public static IReadOnlyList<string> FindMdbFiles(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"No existe la carpeta de origen '{sourceDirectory}'.");
        }

        return Directory
            .EnumerateFiles(sourceDirectory, "*.mdb", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class FileHashCalculator
{
    public static async Task<string> CalculateSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}

public static class ImportDeduplicationPolicy
{
    public static bool ShouldSkip(bool previousSuccessfulImport, bool forceImport)
    {
        return previousSuccessfulImport && !forceImport;
    }
}
