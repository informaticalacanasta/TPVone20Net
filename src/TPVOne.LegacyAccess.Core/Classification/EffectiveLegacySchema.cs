using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Classification;

public static class EffectiveLegacySchema
{
    public static LegacyTableSchema Apply(
        LegacyTableSchema schema,
        IReadOnlyDictionary<string, LegacyBinaryColumnKind> kinds)
    {
        if (kinds.Count == 0)
        {
            return schema;
        }

        var columns = schema.Columns
            .Select(column => ApplyColumn(column, kinds))
            .ToArray();
        return schema with { Columns = columns };
    }

    private static LegacyColumnSchema ApplyColumn(
        LegacyColumnSchema column,
        IReadOnlyDictionary<string, LegacyBinaryColumnKind> kinds)
    {
        if (!kinds.TryGetValue(column.Name, out var kind))
        {
            return column;
        }

        if (kind == LegacyBinaryColumnKind.Utf16Text)
        {
            return column with { SourceTypeName = "dbMemo" };
        }

        return column.SourceTypeName.Trim().ToUpperInvariant() == "DBMEMO"
            ? column with { SourceTypeName = "dbLongBinary" }
            : column;
    }
}
