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
