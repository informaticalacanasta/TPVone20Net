namespace TPVOne.LegacyAccess.Core.Import;

public static class ProtectedInfrastructureTables
{
    public static readonly IReadOnlyList<string> Names =
    [
        "SchemaMigrations",
        "LegacyImportHistory",
        "LegacyAccessConversionHistory"
    ];

    public static bool Contains(string tableName)
    {
        return Names.Contains(tableName, StringComparer.OrdinalIgnoreCase);
    }
}
