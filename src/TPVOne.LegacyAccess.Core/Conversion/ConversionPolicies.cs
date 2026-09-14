using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Conversion;

public static class ConvertedPathResolver
{
    public static string GetConvertedPath(
        string sourceRoot,
        string sourceFile,
        string convertedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(convertedRoot);

        var relative = Path.GetRelativePath(
            Path.GetFullPath(sourceRoot),
            Path.GetFullPath(sourceFile));
        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"El archivo '{sourceFile}' no está dentro de '{sourceRoot}'.");
        }

        return Path.GetFullPath(Path.Combine(convertedRoot, relative));
    }

    public static string GetTemporaryPath(string destinationFile)
    {
        return destinationFile + ".converting";
    }

    public static string GetJet4TemporaryPath(string destinationFile)
    {
        return destinationFile + ".jet4.tmp";
    }
}

public static class ConversionDeduplicationPolicy
{
    public static ConversionAction Decide(
        AccessJetFormat format,
        string currentSourceHash,
        bool destinationExists,
        string? successfulHistorySourceHash,
        bool destinationIsCompleteAccessProject = true)
    {
        if (destinationExists &&
            destinationIsCompleteAccessProject &&
            string.Equals(
                successfulHistorySourceHash,
                currentSourceHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return ConversionAction.SkipAlreadyConverted;
        }

        var needsConvert = AccessFormatDetector.RequiresJet4Conversion(format);
        if (destinationExists)
        {
            return ConversionAction.Reconvert;
        }

        return needsConvert
            ? ConversionAction.ConvertToJet4
            : ConversionAction.CopyCompatible;
    }
}
