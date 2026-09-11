using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Schema;

public sealed class SchemaComparisonService
{
    private readonly AccessToSqlTypeMapper _typeMapper;

    public SchemaComparisonService(AccessToSqlTypeMapper typeMapper)
    {
        _typeMapper = typeMapper;
    }

    public IReadOnlyList<SchemaDifference> Compare(
        AccessTableSchema accessTable,
        SqlTableSchema sqlTable)
    {
        var differences = new List<SchemaDifference>();
        var sqlColumns = sqlTable.Columns.ToDictionary(
            column => column.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var accessColumn in accessTable.Columns)
        {
            if (!sqlColumns.Remove(accessColumn.Name, out var sqlColumn))
            {
                differences.Add(new(
                    SchemaDifferenceKind.MissingColumn,
                    accessColumn.Name,
                    "La columna de Access no existe en SQL Server.",
                    true));
                continue;
            }

            CompareColumn(accessColumn, sqlColumn, differences);
        }

        foreach (var extraColumn in sqlColumns.Values)
        {
            differences.Add(new(
                SchemaDifferenceKind.ExtraColumn,
                extraColumn.Name,
                "La columna solo existe en SQL Server.",
                !extraColumn.IsNullable && !extraColumn.IsIdentity));
        }

        CompareKeysAndIndexes(accessTable, sqlTable, differences);

        if (differences.Count == 0)
        {
            differences.Add(new(
                SchemaDifferenceKind.ExactMatch,
                accessTable.Name,
                "Los esquemas coinciden.",
                false));
        }

        return differences;
    }

    private void CompareColumn(
        AccessColumnSchema access,
        SqlColumnSchema sql,
        ICollection<SchemaDifference> differences)
    {
        var expected = _typeMapper.Map(access);
        if (!string.Equals(
                expected.TypeName,
                sql.Type.TypeName,
                StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new(
                SchemaDifferenceKind.TypeMismatch,
                access.Name,
                $"Access requiere {expected.ToSql()} y SQL tiene {sql.Type.ToSql()}.",
                true));
            return;
        }

        if (expected.TypeName is "nvarchar" or "varbinary" &&
            !CanContain(expected.MaxLength, sql.Type.MaxLength))
        {
            differences.Add(new(
                SchemaDifferenceKind.SizeMismatch,
                access.Name,
                $"SQL {sql.Type.ToSql()} no admite el tamaño {expected.ToSql()}.",
                true));
        }
        else if (expected.TypeName == "decimal" &&
                 (sql.Type.Precision < expected.Precision ||
                  sql.Type.Scale < expected.Scale))
        {
            differences.Add(new(
                SchemaDifferenceKind.SizeMismatch,
                access.Name,
                $"SQL {sql.Type.ToSql()} no admite {expected.ToSql()}.",
                true));
        }
        else if (!Equals(expected, sql.Type))
        {
            differences.Add(new(
                SchemaDifferenceKind.Compatible,
                access.Name,
                $"SQL {sql.Type.ToSql()} puede contener {expected.ToSql()}.",
                false));
        }

        if (access.IsNullable && !sql.IsNullable)
        {
            differences.Add(new(
                SchemaDifferenceKind.NullabilityMismatch,
                access.Name,
                "Access admite NULL pero SQL Server no.",
                true));
        }

        if (access.IsAutoIncrement != sql.IsIdentity)
        {
            differences.Add(new(
                SchemaDifferenceKind.TypeMismatch,
                access.Name,
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

    private static void CompareKeysAndIndexes(
        AccessTableSchema access,
        SqlTableSchema sql,
        ICollection<SchemaDifference> differences)
    {
        var accessPrimary = Signature(access.Indexes.FirstOrDefault(index => index.IsPrimaryKey));
        var sqlPrimary = Signature(sql.Indexes.FirstOrDefault(index => index.IsPrimaryKey));
        if (!string.Equals(accessPrimary, sqlPrimary, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new(
                SchemaDifferenceKind.PrimaryKeyMismatch,
                access.Name,
                "La clave primaria no coincide.",
                false));
        }

        var sqlIndexes = sql.Indexes
            .Where(index => !index.IsPrimaryKey)
            .Select(Signature)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (access.Indexes
            .Where(index => !index.IsPrimaryKey)
            .Select(Signature)
            .Any(signature => !sqlIndexes.Contains(signature)))
        {
            differences.Add(new(
                SchemaDifferenceKind.IndexMismatch,
                access.Name,
                "Uno o varios índices de Access no existen en SQL Server.",
                false));
        }
    }

    private static string Signature(AccessIndexSchema? index)
    {
        return index is null
            ? string.Empty
            : $"{index.IsUnique}:{string.Join(",", index.Columns
                .OrderBy(column => column.Ordinal)
                .Select(column => $"{column.Name}:{column.IsDescending}"))}";
    }
}
