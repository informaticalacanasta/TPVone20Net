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
    public static IReadOnlyList<string> FindMdbFiles(
        string sourceDirectory,
        SearchOption searchOption = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"No existe la carpeta de origen '{sourceDirectory}'.");
        }

        return Directory
            .EnumerateFiles(sourceDirectory, "*.mdb", searchOption)
            .Where(path => !path.EndsWith(".converting", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetRelativePath(sourceDirectory, path), StringComparer.OrdinalIgnoreCase)
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
