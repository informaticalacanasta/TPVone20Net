using System.Security.Cryptography;

namespace TPVOne.LegacyAccess.Core.Utilities;

public static class SqlIdentifier
{
    public const int MaxLength = 128;

    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string Fit(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return identifier.Length <= MaxLength
            ? identifier
            : identifier[..MaxLength];
    }
}

public static class LegacyStagingNames
{
    public static string NewToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string StagingTable(string tableName, string token)
    {
        return SqlIdentifier.Fit($"{tableName}__s{token}");
    }

    public static string BackupTable(string tableName, string token)
    {
        return SqlIdentifier.Fit($"{tableName}__b{token}");
    }

    public static string SuffixedIndex(string indexName, string? suffix)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            return indexName;
        }

        return SqlIdentifier.Fit($"{indexName}__{suffix}");
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

    public static string CalculateSha256(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
