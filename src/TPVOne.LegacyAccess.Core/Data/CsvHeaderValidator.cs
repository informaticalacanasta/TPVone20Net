using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Data;

public static class CsvHeaderValidator
{
    public static void EnsureMatches(
        IReadOnlyList<string> header,
        LegacyTableSchema schema)
    {
        if (header.Count != schema.Columns.Count)
        {
            throw new CsvHeaderMismatchException(
                $"La cabecera de '{schema.Name}' tiene {header.Count} columnas " +
                $"y el esquema declara {schema.Columns.Count}.");
        }

        for (var index = 0; index < header.Count; index++)
        {
            var expected = schema.Columns[index].Name;
            if (!string.Equals(header[index], expected, StringComparison.Ordinal))
            {
                throw new CsvHeaderMismatchException(
                    $"La cabecera de '{schema.Name}' no coincide en la posición {index + 1}: " +
                    $"CSV='{header[index]}', TXT='{expected}'.");
            }
        }
    }
}
