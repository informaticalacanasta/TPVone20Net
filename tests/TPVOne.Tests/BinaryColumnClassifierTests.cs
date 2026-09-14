using System.Text;
using TPVOne.LegacyAccess.Core.Classification;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.Tests;

public sealed class BinaryColumnClassifierTests
{
    private readonly BinaryColumnClassifier _classifier = new();

    [Fact]
    public void SimpleUtf16Le_IsText()
    {
        var kind = Classify("notas", "dbLongBinary", [Utf16("Ingredientes")]);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
    }

    [Fact]
    public void SpanishAccents_AreText()
    {
        var kind = Classify(
            "notas",
            "dbLongBinary",
            [Utf16("jamón de york untado con paté")]);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
    }

    [Fact]
    public void MultilineText_IsText()
    {
        var kind = Classify(
            "notas",
            "dbLongBinary",
            [Utf16("PAN DE CENTENO\r\n\r\nPan de molde de CENTENO.")]);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
    }

    [Fact]
    public void SingleSpace_IsText()
    {
        var kind = Classify("notas", "dbLongBinary", [Utf16(" ")]);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
    }

    [Fact]
    public void CrLf_IsText()
    {
        var kind = Classify("notas", "dbLongBinary", [Utf16("\r\n")]);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
    }

    [Fact]
    public void ValidBmp_IsBinary()
    {
        var kind = Classify("imagen", "dbLongBinary", [Convert.ToBase64String(MinimalBmp())]);
        Assert.Equal(LegacyBinaryColumnKind.Binary, kind);
    }

    [Fact]
    public void RandomBytes_AreBinary()
    {
        var random = Enumerable.Range(0, 64).Select(value => (byte)(value * 17 + 3)).ToArray();
        var kind = Classify("payload", "dbLongBinary", [Convert.ToBase64String(random)]);
        Assert.Equal(LegacyBinaryColumnKind.Binary, kind);
    }

    [Fact]
    public void OddLength_IsBinaryDuringAnalysis()
    {
        var kind = Classify("payload", "dbLongBinary", [Convert.ToBase64String([0x41, 0x00, 0x42])]);
        Assert.Equal(LegacyBinaryColumnKind.Binary, kind);
    }

    [Fact]
    public void MajorityText_WithOneEmpty_IsText()
    {
        var values = Enumerable.Repeat(Utf16("texto"), 19).Append("").ToArray();
        var kind = Classify("notas", "dbLongBinary", values);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
    }

    [Fact]
    public void MixedHalfAndHalf_StaysBinary()
    {
        var values = Enumerable.Repeat(Utf16("texto"), 10)
            .Concat(Enumerable.Repeat(Convert.ToBase64String(MinimalBmp()), 10))
            .ToArray();
        var kind = Classify("payload", "dbLongBinary", values);
        Assert.Equal(LegacyBinaryColumnKind.Binary, kind);
    }

    [Fact]
    public void Composicio_IsTextWithoutNameHardcode()
    {
        var kind = Classify("COMPOSICIO", "dbLongBinary", [Utf16("PAN DE CENTENO ALEMAN")]);
        Assert.Equal(LegacyBinaryColumnKind.Utf16Text, kind);
        Assert.False(BinaryColumnClassifier.LooksPhotographic("COMPOSICIO"));
    }

    [Fact]
    public void FotoName_StaysBinaryEvenIfContentLooksLikeText()
    {
        var kind = Classify("FOTO", "dbLongBinary", [Utf16("no soy una foto")]);
        Assert.Equal(LegacyBinaryColumnKind.Binary, kind);
    }

    [Fact]
    public void EffectiveSchema_MapsUtf16BinaryToMemo()
    {
        var schema = new LegacyTableSchema(
            "articulos",
            [
                new("id", "dbLong", 4, null, null, 0, true, false),
                new("COMPOSICIO", "dbLongBinary", null, null, null, 1, true, false),
                new("FOTO", "dbLongBinary", null, null, null, 2, true, false)
            ],
            []);
        var kinds = new Dictionary<string, LegacyBinaryColumnKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["COMPOSICIO"] = LegacyBinaryColumnKind.Utf16Text,
            ["FOTO"] = LegacyBinaryColumnKind.Binary
        };

        var effective = EffectiveLegacySchema.Apply(schema, kinds);
        var mapper = new TPVOne.LegacyAccess.Core.Mapping.DaoToSqlTypeMapper();

        Assert.Equal("dbMemo", effective.Columns[1].SourceTypeName);
        Assert.Equal("nvarchar(max)", mapper.Map(effective.Columns[1]).ToSql());
        Assert.Equal("dbLongBinary", effective.Columns[2].SourceTypeName);
        Assert.Equal("varbinary(max)", mapper.Map(effective.Columns[2]).ToSql());
    }

    private LegacyBinaryColumnKind Classify(string name, string daoType, IReadOnlyList<string> values)
    {
        return _classifier.ClassifyColumn(
            new LegacyColumnSchema(name, daoType, null, null, null, 0, true, false),
            values);
    }

    private static string Utf16(string text)
    {
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
    }

    private static byte[] MinimalBmp()
    {
        var data = new byte[58];
        data[0] = 0x42;
        data[1] = 0x4D;
        BitConverter.GetBytes(58).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(1).CopyTo(data, 18);
        BitConverter.GetBytes(1).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        return data;
    }
}
