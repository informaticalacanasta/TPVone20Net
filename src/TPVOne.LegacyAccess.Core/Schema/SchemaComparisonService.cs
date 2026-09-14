using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Schema;

public sealed class SchemaComparisonService
{
    private readonly DaoToSqlTypeMapper _typeMapper;

    public SchemaComparisonService(DaoToSqlTypeMapper typeMapper)
    {
        _typeMapper = typeMapper;
    }

    public IReadOnlyList<SchemaDifference> Compare(
        LegacyTableSchema sourceTable,
        SqlTableSchema sqlTable)
    {
        var differences = new List<SchemaDifference>();
        var sqlColumns = sqlTable.Columns.ToDictionary(
            column => column.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var sourceColumn in sourceTable.Columns)
        {
            if (!sqlColumns.Remove(sourceColumn.Name, out var sqlColumn))
            {
                differences.Add(new(
                    SchemaDifferenceKind.MissingColumn,
                    sourceColumn.Name,
                    "La columna de origen no existe en SQL Server.",
                    true));
                continue;
            }

            CompareColumn(sourceColumn, sqlColumn, differences);
        }

        foreach (var extraColumn in sqlColumns.Values)
        {
            differences.Add(new(
                SchemaDifferenceKind.ExtraColumn,
                extraColumn.Name,
                "La columna solo existe en SQL Server.",
                !extraColumn.IsNullable && !extraColumn.IsIdentity));
        }

        CompareKeysAndIndexes(sourceTable, sqlTable, differences);

        if (differences.Count == 0)
        {
            differences.Add(new(
                SchemaDifferenceKind.ExactMatch,
                sourceTable.Name,
                "Los esquemas coinciden.",
                false));
        }

        return differences;
    }

    private void CompareColumn(
        LegacyColumnSchema source,
        SqlColumnSchema sql,
        ICollection<SchemaDifference> differences)
    {
        var expected = _typeMapper.Map(source);
        if (!string.Equals(
                expected.TypeName,
                sql.Type.TypeName,
                StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new(
                SchemaDifferenceKind.TypeMismatch,
                source.Name,
                $"El origen requiere {expected.ToSql()} y SQL tiene {sql.Type.ToSql()}.",
                true));
            return;
        }

        if (expected.TypeName is "nvarchar" or "varbinary" &&
            !CanContain(expected.MaxLength, sql.Type.MaxLength))
        {
            differences.Add(new(
                SchemaDifferenceKind.SizeMismatch,
                source.Name,
                $"SQL {sql.Type.ToSql()} no admite el tamaño {expected.ToSql()}.",
                true));
        }
        else if (expected.TypeName == "decimal" &&
                 (sql.Type.Precision < expected.Precision ||
                  sql.Type.Scale < expected.Scale))
        {
            differences.Add(new(
                SchemaDifferenceKind.SizeMismatch,
                source.Name,
                $"SQL {sql.Type.ToSql()} no admite {expected.ToSql()}.",
                true));
        }
        else if (!AreEquivalent(expected, sql.Type))
        {
            differences.Add(new(
                SchemaDifferenceKind.Compatible,
                source.Name,
                $"SQL {sql.Type.ToSql()} puede contener {expected.ToSql()}.",
                false));
        }

        if (source.IsNullable && !sql.IsNullable)
        {
            differences.Add(new(
                SchemaDifferenceKind.NullabilityMismatch,
                source.Name,
                "El origen admite NULL pero SQL Server no.",
                true));
        }

        if (source.IsAutoIncrement != sql.IsIdentity)
        {
            differences.Add(new(
                SchemaDifferenceKind.TypeMismatch,
                source.Name,
                "La configuración AutoNumber/IDENTITY no coincide.",
                true));
        }
    }

    private static bool CanContain(int? sourceLength, int? destinationLength)
    {
        return destinationLength is < 0 ||
            sourceLength is not null &&
            destinationLength is not null &&
            destinationLength >= sourceLength;
    }

    private static bool AreEquivalent(SqlTypeDefinition expected, SqlTypeDefinition actual)
    {
        if (!string.Equals(expected.TypeName, actual.TypeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expected.TypeName is "nvarchar" or "varbinary")
        {
            return NormalizeLength(expected.MaxLength) == NormalizeLength(actual.MaxLength);
        }

        if (expected.TypeName == "decimal")
        {
            return expected.Precision == actual.Precision && expected.Scale == actual.Scale;
        }

        return true;
    }

    private static int NormalizeLength(int? length)
    {
        return length is null or < 0 ? -1 : length.Value;
    }

    private static void CompareKeysAndIndexes(
        LegacyTableSchema source,
        SqlTableSchema sql,
        ICollection<SchemaDifference> differences)
    {
        var sourcePrimary = Signature(source.Indexes.FirstOrDefault(index => index.IsPrimaryKey));
        var sqlPrimary = Signature(sql.Indexes.FirstOrDefault(index => index.IsPrimaryKey));
        if (!string.Equals(sourcePrimary, sqlPrimary, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new(
                SchemaDifferenceKind.PrimaryKeyMismatch,
                source.Name,
                "La clave primaria no coincide.",
                false));
        }

        var sqlIndexes = sql.Indexes
            .Where(index => !index.IsPrimaryKey)
            .Select(Signature)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (source.Indexes
            .Where(index => !index.IsPrimaryKey)
            .Select(Signature)
            .Any(signature => !sqlIndexes.Contains(signature)))
        {
            differences.Add(new(
                SchemaDifferenceKind.IndexMismatch,
                source.Name,
                "Uno o varios índices de origen no existen en SQL Server.",
                false));
        }
    }

    private static string Signature(LegacyIndexSchema? index)
    {
        return index is null
            ? string.Empty
            : $"{index.IsUnique}:{string.Join(",", index.Columns
                .OrderBy(column => column.Ordinal)
                .Select(column => $"{column.Name}:{column.IsDescending}"))}";
    }
}
