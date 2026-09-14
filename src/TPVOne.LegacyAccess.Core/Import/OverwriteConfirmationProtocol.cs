namespace TPVOne.LegacyAccess.Core.Import;

public static class OverwriteConfirmationProtocol
{
    public const string RequestPrefix = "TPVONE_OVERWRITE?:";

    public static string Prompt(string tableName)
    {
        return $"La tabla '{tableName}' ya existe. ¿Sobrescribir? [S/N]: ";
    }

    public static string Conserved(string tableName)
    {
        return $"Se conserva la tabla '{tableName}'.";
    }

    public static string RequestLine(string tableName)
    {
        return RequestPrefix + tableName;
    }

    public static bool TryParseRequest(string line, out string tableName)
    {
        if (line.StartsWith(RequestPrefix, StringComparison.Ordinal))
        {
            tableName = line[RequestPrefix.Length..];
            return tableName.Length > 0;
        }

        tableName = string.Empty;
        return false;
    }
}
