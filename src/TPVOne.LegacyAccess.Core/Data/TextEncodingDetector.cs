using System.Text;

namespace TPVOne.LegacyAccess.Core.Data;

public static class TextEncodingDetector
{
    static TextEncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Resolve(string filePath, string defaultEncodingName)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024,
            FileOptions.SequentialScan);
        Span<byte> bom = stackalloc byte[3];
        var read = stream.Read(bom);
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        return Encoding.GetEncoding(defaultEncodingName);
    }
}
