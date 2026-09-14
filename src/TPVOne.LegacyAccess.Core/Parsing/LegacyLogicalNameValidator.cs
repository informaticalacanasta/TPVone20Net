using TPVOne.LegacyAccess.Core.Exceptions;

namespace TPVOne.LegacyAccess.Core.Parsing;

public static class LegacyLogicalNameValidator
{
    public static void EnsureMatchesFile(string filePath, string tableName)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (!string.Equals(fileName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new SchemaNameMismatchException(
                $"El archivo '{Path.GetFileName(filePath)}' declara " +
                $"Nombre Tabla='{tableName}'. El nombre lógico debe coincidir.");
        }
    }
}
