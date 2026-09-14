using System.Text;
using TPVOne.LegacyAccess.Core.Binary;

namespace TPVOne.LegacyAccess.Core.Classification;

public static class Utf16LeTextCodec
{
    private static readonly UnicodeEncoding Utf16Le = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length % 2 != 0)
        {
            throw new FormatException(
                $"Longitud {bytes.Length} inválida para UTF-16LE.");
        }

        return Normalize(Utf16Le.GetString(bytes));
    }

    public static string DecodeBase64(string raw)
    {
        return Decode(Base64Decoder.Decode(raw));
    }

    public static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length % 2 != 0)
        {
            return false;
        }

        if (!HasUtf16LeLatinPattern(bytes))
        {
            return false;
        }

        try
        {
            var text = Normalize(Utf16Le.GetString(bytes));
            return text.Length > 0 && IsMostlyReadable(text);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsEmptyPayload(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return true;
        }

        if (bytes.Length % 2 != 0)
        {
            return false;
        }

        try
        {
            return Normalize(Utf16Le.GetString(bytes)).Length == 0;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Normalize(string text)
    {
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return text.TrimEnd('\0');
    }

    private static bool HasUtf16LeLatinPattern(byte[] bytes)
    {
        var units = bytes.Length / 2;
        var highZero = 0;
        for (var index = 1; index < bytes.Length; index += 2)
        {
            if (bytes[index] == 0)
            {
                highZero++;
            }
        }

        return highZero * 2 >= units;
    }

    private static bool IsMostlyReadable(string text)
    {
        var readable = 0;
        foreach (var ch in text)
        {
            if (IsAllowed(ch))
            {
                readable++;
            }
        }

        return readable * 10 >= text.Length * 9;
    }

    private static bool IsAllowed(char ch)
    {
        if (ch is '\r' or '\n' or '\t')
        {
            return true;
        }

        if (char.IsControl(ch))
        {
            return false;
        }

        return char.IsLetterOrDigit(ch)
            || char.IsPunctuation(ch)
            || char.IsSymbol(ch)
            || char.IsWhiteSpace(ch);
    }
}
