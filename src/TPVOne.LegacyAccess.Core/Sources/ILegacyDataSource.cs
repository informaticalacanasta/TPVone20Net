using System.Data;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Sources;

public interface ILegacyDataSource
{
    string Location { get; }

    IReadOnlyList<string> ReadHeader();

    long CountRecords();

    IDataReader OpenReader(LegacyTableSchema schema);

    IReadOnlyDictionary<int, IReadOnlyList<string>> SampleNonEmptyValues(
        IReadOnlyCollection<int> ordinals,
        int targetPerColumn,
        int maxRows);
}
