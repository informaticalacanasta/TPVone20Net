using System.Text.RegularExpressions;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Parsing;

public sealed class TxtTableStructureParser
{
    private static readonly Regex TableNameRegex = new(
        @"^\s*Nombre\s+Tabla\s*[=:]\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FieldRegex = new(
        @"Nombre\s+Campo\s*[=:]\s*(.+?)\s+Tipo\s*[=:]\s*(db\w+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SizeRegex = new(
        @"\bsize\s*[=:]\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrecisionRegex = new(
        @"\bprecision\s*[=:]\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ScaleRegex = new(
        @"\bscale\s*[=:]\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexNameRegex = new(
        @"^\s*Nombre\s+Indice\s*[=:]\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrimaryRegex = new(
        @"^\s*Es\s+Primary\s*[=:]\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UniqueRegex = new(
        @"^\s*Es\s+Unique\s*[=:]\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexFieldRegex = new(
        @"^\s*-\s*(.+?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public LegacyTableSchema Parse(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        string? tableName = null;
        var columns = new List<LegacyColumnSchema>();
        var indexes = new List<LegacyIndexSchema>();
        IndexDraft? currentIndex = null;
        var readingIndexFields = false;

        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (IsSection(line, "Estructura"))
            {
                FlushIndex();
                readingIndexFields = false;
                continue;
            }

            if (IsSection(line, "Indices") || IsSection(line, "Índices"))
            {
                FlushIndex();
                readingIndexFields = false;
                continue;
            }

            var tableMatch = TableNameRegex.Match(rawLine);
            if (tableMatch.Success)
            {
                tableName = tableMatch.Groups[1].Value.Trim();
                continue;
            }

            var fieldMatch = FieldRegex.Match(rawLine);
            if (fieldMatch.Success)
            {
                FlushIndex();
                readingIndexFields = false;
                columns.Add(ReadColumn(rawLine, fieldMatch, columns.Count));
                continue;
            }

            var indexMatch = IndexNameRegex.Match(rawLine);
            if (indexMatch.Success)
            {
                FlushIndex();
                currentIndex = new IndexDraft(indexMatch.Groups[1].Value.Trim());
                readingIndexFields = false;
                continue;
            }

            var primaryMatch = PrimaryRegex.Match(rawLine);
            if (primaryMatch.Success && currentIndex is not null)
            {
                currentIndex.IsPrimaryKey = ParseBoolean(primaryMatch.Groups[1].Value);
                continue;
            }

            var uniqueMatch = UniqueRegex.Match(rawLine);
            if (uniqueMatch.Success && currentIndex is not null)
            {
                currentIndex.IsUnique = ParseBoolean(uniqueMatch.Groups[1].Value);
                continue;
            }

            if (line.StartsWith("Campos", StringComparison.OrdinalIgnoreCase))
            {
                readingIndexFields = currentIndex is not null;
                continue;
            }

            if (readingIndexFields && currentIndex is not null)
            {
                var fieldNameMatch = IndexFieldRegex.Match(rawLine);
                if (fieldNameMatch.Success)
                {
                    currentIndex.Columns.Add(new LegacyIndexColumn(
                        fieldNameMatch.Groups[1].Value.Trim(),
                        currentIndex.Columns.Count,
                        false));
                    continue;
                }
            }
        }

        FlushIndex();

        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new SchemaExtractionException("El TXT no declara 'Nombre Tabla'.");
        }

        if (columns.Count == 0)
        {
            throw new SchemaExtractionException(
                $"La tabla '{tableName}' no declara columnas.");
        }

        foreach (var index in indexes)
        {
            if (index.Columns.Count == 0)
            {
                throw new SchemaExtractionException(
                    $"El índice '{index.Name}' de '{tableName}' no declara campos.");
            }
        }

        return new(tableName, columns, indexes);

        void FlushIndex()
        {
            if (currentIndex is null)
            {
                return;
            }

            indexes.Add(new(
                currentIndex.Name,
                currentIndex.IsUnique,
                currentIndex.IsPrimaryKey,
                currentIndex.Columns));
            currentIndex = null;
            readingIndexFields = false;
        }
    }

    private static LegacyColumnSchema ReadColumn(
        string line,
        Match fieldMatch,
        int ordinal)
    {
        var name = fieldMatch.Groups[1].Value.Trim();
        var sourceType = fieldMatch.Groups[2].Value.Trim();
        var sizeMatch = SizeRegex.Match(line);
        var precisionMatch = PrecisionRegex.Match(line);
        var scaleMatch = ScaleRegex.Match(line);

        return new(
            name,
            sourceType,
            sizeMatch.Success ? int.Parse(sizeMatch.Groups[1].Value) : null,
            precisionMatch.Success ? byte.Parse(precisionMatch.Groups[1].Value) : null,
            scaleMatch.Success ? byte.Parse(scaleMatch.Groups[1].Value) : null,
            ordinal,
            true,
            false);
    }

    private static bool IsSection(string line, string name)
    {
        return line.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            line.Equals(name + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ParseBoolean(string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("si", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("sí", StringComparison.OrdinalIgnoreCase) ||
            normalized == "1")
        {
            return true;
        }

        if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            normalized == "0")
        {
            return false;
        }

        throw new SchemaExtractionException(
            $"No se pudo interpretar el valor booleano '{value}'.");
    }

    private sealed class IndexDraft
    {
        public IndexDraft(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public List<LegacyIndexColumn> Columns { get; } = [];
    }
}
