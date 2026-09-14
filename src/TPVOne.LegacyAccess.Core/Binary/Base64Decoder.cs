using System.Text;

namespace TPVOne.LegacyAccess.Core.Binary;

public static class Base64Decoder
{
    public static byte[] Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var compact = StripWhitespace(value);
        return Convert.FromBase64String(compact);
    }

    private static string StripWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
