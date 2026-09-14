using System.Data;
using TPVOne.LegacyAccess.Core.Data;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Sources;

public sealed class CsvLegacyDataSource : ILegacyDataSource
{
    private readonly string _defaultEncodingName;
    private readonly char _delimiter;
    private readonly LegacyValueConverter _converter;

    public CsvLegacyDataSource(
        string location,
        string defaultEncodingName = "windows-1252",
        char delimiter = '|',
        LegacyValueConverter? converter = null)
    {
        Location = location;
        _defaultEncodingName = defaultEncodingName;
        _delimiter = delimiter;
        _converter = converter ?? new LegacyValueConverter();
    }

    public string Location { get; }

    public IReadOnlyList<string> ReadHeader()
    {
        using var reader = OpenRecordReader();
        return reader.ReadHeader()
            ?? throw new InvalidDataException($"El CSV '{Location}' no contiene cabecera.");
    }

    public long CountRecords()
    {
        using var reader = OpenRecordReader();
        var header = reader.ReadHeader()
            ?? throw new InvalidDataException($"El CSV '{Location}' no contiene cabecera.");
        long count = 0;
        while (reader.ReadRecord(header.Count) is not null)
        {
            count++;
        }

        return count;
    }

    public IDataReader OpenReader(LegacyTableSchema schema)
    {
        return new CsvLegacyDataReader(OpenRecordReader(), schema, _converter);
    }

    public IReadOnlyDictionary<int, IReadOnlyList<string>> SampleNonEmptyValues(
        IReadOnlyCollection<int> ordinals,
        int targetPerColumn,
        int maxRows)
    {
        var collected = ordinals.Distinct().ToDictionary(
            ordinal => ordinal,
            _ => new List<string>());
        if (collected.Count == 0)
        {
            return collected.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value);
        }

        using var reader = OpenRecordReader();
        var header = reader.ReadHeader()
            ?? throw new InvalidDataException($"El CSV '{Location}' no contiene cabecera.");
        var rows = 0;
        while (rows < maxRows && collected.Values.Any(values => values.Count < targetPerColumn))
        {
            var record = reader.ReadRecord(header.Count);
            if (record is null)
            {
                break;
            }

            rows++;
            foreach (var ordinal in collected.Keys.ToArray())
            {
                if (ordinal < 0 || ordinal >= record.Count)
                {
                    continue;
                }

                if (collected[ordinal].Count >= targetPerColumn)
                {
                    continue;
                }

                var value = record[ordinal];
                if (value.Length > 0)
                {
                    collected[ordinal].Add(value);
                }
            }
        }

        return collected.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value);
    }

    private PipeDelimitedRecordReader OpenRecordReader()
    {
        var encoding = TextEncodingDetector.Resolve(Location, _defaultEncodingName);
        var stream = new FileStream(
            Location,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 64,
            FileOptions.SequentialScan);
        return new PipeDelimitedRecordReader(stream, encoding, _delimiter);
    }
}
