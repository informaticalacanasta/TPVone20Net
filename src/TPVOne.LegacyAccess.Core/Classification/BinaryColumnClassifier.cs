using TPVOne.LegacyAccess.Core.Binary;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Sources;

namespace TPVOne.LegacyAccess.Core.Classification;

public sealed class BinaryColumnClassifier : IBinaryColumnClassifier
{
    public const int TargetNonEmptySamples = 50;
    public const int MaxRowsToScan = 2000;

    public IReadOnlyDictionary<string, LegacyBinaryColumnKind> Classify(
        LegacyTableSchema schema,
        ILegacyDataSource? dataSource)
    {
        var classifiable = schema.Columns
            .Where(IsClassifiable)
            .ToArray();
        if (classifiable.Length == 0)
        {
            return new Dictionary<string, LegacyBinaryColumnKind>(StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyDictionary<int, IReadOnlyList<string>> samples =
            dataSource is null
                ? new Dictionary<int, IReadOnlyList<string>>()
                : dataSource.SampleNonEmptyValues(
                    classifiable.Select(column => column.Ordinal).ToArray(),
                    TargetNonEmptySamples,
                    MaxRowsToScan);

        var result = new Dictionary<string, LegacyBinaryColumnKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in classifiable)
        {
            samples.TryGetValue(column.Ordinal, out var values);
            result[column.Name] = ClassifyColumn(column, values ?? []);
        }

        return result;
    }

    public LegacyBinaryColumnKind ClassifyColumn(
        LegacyColumnSchema column,
        IReadOnlyList<string> rawNonEmptyValues)
    {
        if (LooksPhotographic(column.Name))
        {
            return LegacyBinaryColumnKind.Binary;
        }

        var textVotes = 0;
        var binaryVotes = 0;
        foreach (var raw in rawNonEmptyValues)
        {
            byte[] bytes;
            try
            {
                bytes = Base64Decoder.Decode(raw);
            }
            catch (FormatException)
            {
                binaryVotes++;
                continue;
            }

            if (bytes.Length == 0 || Utf16LeTextCodec.IsEmptyPayload(bytes))
            {
                continue;
            }

            if (IsBmp(bytes))
            {
                return LegacyBinaryColumnKind.Binary;
            }

            if (Utf16LeTextCodec.LooksLikeText(bytes))
            {
                textVotes++;
            }
            else
            {
                binaryVotes++;
            }
        }

        var evidence = textVotes + binaryVotes;
        if (evidence == 0)
        {
            return DefaultKind(column);
        }

        if (evidence == 1)
        {
            return textVotes == 1
                ? LegacyBinaryColumnKind.Utf16Text
                : LegacyBinaryColumnKind.Binary;
        }

        if (evidence <= 3)
        {
            return binaryVotes == 0
                ? LegacyBinaryColumnKind.Utf16Text
                : LegacyBinaryColumnKind.Binary;
        }

        return textVotes * 10 >= evidence * 9
            ? LegacyBinaryColumnKind.Utf16Text
            : LegacyBinaryColumnKind.Binary;
    }

    public static bool IsClassifiable(LegacyColumnSchema column)
    {
        var type = column.SourceTypeName.Trim().ToUpperInvariant();
        return type is "DBBINARY" or "DBLONGBINARY" or "DBMEMO";
    }

    public static bool LooksPhotographic(string columnName)
    {
        var name = columnName.ToUpperInvariant();
        return name.Contains("FOTO", StringComparison.Ordinal)
            || name.Contains("PHOTO", StringComparison.Ordinal)
            || name.Contains("IMAGEN", StringComparison.Ordinal)
            || name.Contains("IMAGE", StringComparison.Ordinal);
    }

    private static LegacyBinaryColumnKind DefaultKind(LegacyColumnSchema column)
    {
        return column.SourceTypeName.Trim().ToUpperInvariant() == "DBMEMO"
            ? LegacyBinaryColumnKind.Utf16Text
            : LegacyBinaryColumnKind.Binary;
    }

    private static bool IsBmp(byte[] bytes)
    {
        var interpreted = LegacyBinaryPayloadInterpreter.Interpret(bytes);
        return interpreted.Length >= 2
            && interpreted[0] == 0x42
            && interpreted[1] == 0x4D
            && LegacyBinaryPayloadInterpreter.TryReadValidBmp(interpreted, 0, out _);
    }
}
