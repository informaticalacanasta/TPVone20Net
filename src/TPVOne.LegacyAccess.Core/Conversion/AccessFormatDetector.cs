using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Conversion;

public static class AccessFormatDetector
{
    public static AccessJetFormat Detect(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var header = new byte[24];
        var read = stream.Read(header, 0, header.Length);
        if (read < 24)
        {
            return AccessJetFormat.Unknown;
        }

        var magic = System.Text.Encoding.ASCII.GetString(header, 4, 15);
        if (magic.StartsWith("Standard ACE DB", StringComparison.Ordinal))
        {
            return AccessJetFormat.AceAccdb;
        }

        if (!magic.StartsWith("Standard Jet DB", StringComparison.Ordinal))
        {
            return AccessJetFormat.Unknown;
        }

        return header[0x14] switch
        {
            0 => AccessJetFormat.Jet3Access97,
            1 or 2 => AccessJetFormat.Jet4Access2000,
            _ => AccessJetFormat.Unknown
        };
    }

    public static string Describe(AccessJetFormat format)
    {
        return format switch
        {
            AccessJetFormat.Jet3Access97 => "Access 97 / Jet 3",
            AccessJetFormat.Jet4Access2000 => "Access 2000-2003 / Jet 4",
            AccessJetFormat.AceAccdb => "ACE / ACCDB",
            _ => "desconocido"
        };
    }

    public static bool RequiresJet4Conversion(AccessJetFormat format)
    {
        return format is AccessJetFormat.Jet3Access97 or AccessJetFormat.Unknown;
    }

    public static bool HasAccessApplicationCatalog(IEnumerable<string> tableNames)
    {
        return tableNames.Any(name =>
            name.Equals("MSysAccessObjects", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("MSysAccessStorage", StringComparison.OrdinalIgnoreCase));
    }
}
