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
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        return Normalize(Utf16Le.GetString(NormalizeLegacyUtf16Le(bytes)));
    }

    public static string DecodeBase64(string raw)
    {
        return Decode(Base64Decoder.Decode(raw));
    }

    public static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        var candidate = NormalizeLegacyUtf16Le(bytes);
        if (!HasUtf16LeLatinPattern(candidate))
        {
            return false;
        }

        try
        {
            var text = Normalize(Utf16Le.GetString(candidate));
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

        try
        {
            return Normalize(Utf16Le.GetString(NormalizeLegacyUtf16Le(bytes))).Length == 0;
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

    private static byte[] NormalizeLegacyUtf16Le(byte[] bytes)
    {
        if (bytes.Length % 2 == 0)
        {
            return bytes;
        }

        var normalized = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, normalized, 0, bytes.Length);
        normalized[^1] = 0x00;
        return normalized;
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
