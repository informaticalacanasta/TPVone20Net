namespace TPVOne.LegacyAccess.Core.Models;

public sealed record LegacyTableSchema(
    string Name,
    IReadOnlyList<LegacyColumnSchema> Columns,
    IReadOnlyList<LegacyIndexSchema> Indexes,
    long RowCount = 0);

public sealed record LegacyColumnSchema(
    string Name,
    string SourceTypeName,
    int? Size,
    byte? Precision,
    byte? Scale,
    int Ordinal,
    bool IsNullable,
    bool IsAutoIncrement);

public sealed record LegacyIndexSchema(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<LegacyIndexColumn> Columns);

public sealed record LegacyIndexColumn(
    string Name,
    int Ordinal,
    bool IsDescending);

public sealed record SqlTypeDefinition(
    string TypeName,
    int? MaxLength = null,
    byte? Precision = null,
    byte? Scale = null)
{
    public string ToSql()
    {
        return TypeName switch
        {
            "nvarchar" or "varbinary" =>
                $"{TypeName}({(MaxLength is null or < 0 ? "max" : MaxLength.Value)})",
            "decimal" => $"decimal({Precision ?? 18},{Scale ?? 0})",
            _ => TypeName
        };
    }
}

public enum SchemaDifferenceKind
{
    ExactMatch,
    Compatible,
    MissingColumn,
    ExtraColumn,
    TypeMismatch,
    SizeMismatch,
    NullabilityMismatch,
    PrimaryKeyMismatch,
    IndexMismatch
}

public sealed record SchemaDifference(
    SchemaDifferenceKind Kind,
    string Subject,
    string Message,
    bool BlocksImport);

public sealed record SqlColumnSchema(
    string Name,
    SqlTypeDefinition Type,
    bool IsNullable,
    bool IsIdentity,
    int Ordinal);

public sealed record SqlTableSchema(
    string Name,
    IReadOnlyList<SqlColumnSchema> Columns,
    IReadOnlyList<LegacyIndexSchema> Indexes);
