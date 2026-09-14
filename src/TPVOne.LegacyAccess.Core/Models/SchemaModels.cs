using System.Data.OleDb;

namespace TPVOne.LegacyAccess.Core.Models;

public sealed record AccessDatabaseSchema(
    string FilePath,
    string SourceHash,
    string Provider,
    IReadOnlyList<AccessTableSchema> Tables,
    IReadOnlyList<string> Warnings,
    string? OriginalFilePath = null);

public sealed record AccessTableSchema(
    string Name,
    IReadOnlyList<AccessColumnSchema> Columns,
    IReadOnlyList<AccessIndexSchema> Indexes,
    long RowCount);

public sealed record AccessColumnSchema(
    string Name,
    OleDbType ProviderType,
    string ClrTypeName,
    int? MaxLength,
    byte? Precision,
    byte? Scale,
    bool IsNullable,
    bool IsAutoIncrement,
    int Ordinal,
    string? DefaultValue);

public sealed record AccessIndexSchema(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<AccessIndexColumn> Columns);

public sealed record AccessIndexColumn(
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
    IReadOnlyList<AccessIndexSchema> Indexes);
