using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Sources;

public sealed class FileLegacyTableSourceScanner : ILegacyTableSourceScanner
{
    private readonly LegacyImportOptions _options;

    public FileLegacyTableSourceScanner(LegacyImportOptions options)
    {
        _options = options;
    }

    public LegacyDiscoveryResult Discover(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"No existe la carpeta de origen '{sourceDirectory}'.");
        }

        var schemaFiles = EnumerateFiles(sourceDirectory, "*.txt");
        var dataFiles = EnumerateFiles(sourceDirectory, "*.csv");
        var usedDataFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<LegacyTableSource>();
        var warnings = new List<string>();
        var errors = new List<string>();

        var schemaGroups = schemaFiles
            .GroupBy(
                path => LogicalKey(sourceDirectory, path),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in schemaGroups)
        {
            if (group.Count() > 1)
            {
                errors.Add(
                    $"Hay varios TXT ambiguos para '{group.Key}': " +
                    string.Join("; ", group));
                continue;
            }

            var schemaFile = group.Single();
            var logicalName = Path.GetFileNameWithoutExtension(schemaFile);
            var dataFile = FindAssociatedDataFile(schemaFile, dataFiles);
            if (dataFile is not null)
            {
                if (!usedDataFiles.Add(dataFile))
                {
                    errors.Add($"El CSV '{dataFile}' está asociado a más de un TXT.");
                    continue;
                }
            }

            ILegacyDataSource? dataSource = dataFile is null
                ? null
                : new CsvLegacyDataSource(
                    dataFile,
                    _options.DefaultEncoding,
                    ResolveDelimiter(_options.Delimiter));

            sources.Add(new(
                logicalName,
                new TxtLegacySchemaSource(schemaFile),
                dataSource));
        }

        var orphanDataFiles = dataFiles
            .Where(path => !usedDataFiles.Contains(path))
            .ToArray();
        foreach (var orphan in orphanDataFiles)
        {
            errors.Add($"CSV huérfano sin TXT de esquema: {orphan}");
        }

        return new(sources, orphanDataFiles, warnings, errors);
    }

    public static IReadOnlyList<string> FindSchemaFiles(string sourceDirectory)
    {
        return EnumerateFiles(sourceDirectory, "*.txt");
    }

    private static string? FindAssociatedDataFile(
        string schemaFile,
        IReadOnlyList<string> dataFiles)
    {
        var directory = Path.GetDirectoryName(schemaFile) ?? string.Empty;
        var basename = Path.GetFileNameWithoutExtension(schemaFile);
        var matches = dataFiles
            .Where(path =>
                string.Equals(
                    Path.GetDirectoryName(path),
                    directory,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    basename,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Hay varios CSV ambiguos para '{schemaFile}': " +
                string.Join("; ", matches));
        }

        return matches.Length == 1 ? matches[0] : null;
    }

    private static IReadOnlyList<string> EnumerateFiles(string sourceDirectory, string pattern)
    {
        return Directory
            .EnumerateFiles(sourceDirectory, pattern, SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(sourceDirectory, path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string LogicalKey(string sourceDirectory, string path)
    {
        var relative = Path.GetRelativePath(sourceDirectory, path);
        var directory = Path.GetDirectoryName(relative) ?? string.Empty;
        var logicalName = Path.GetFileNameWithoutExtension(path);
        return Path.Combine(directory, logicalName);
    }

    private static char ResolveDelimiter(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return '|';
        }

        return delimiter[0];
    }
}
