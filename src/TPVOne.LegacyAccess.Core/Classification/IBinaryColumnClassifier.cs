using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Sources;

namespace TPVOne.LegacyAccess.Core.Classification;

public interface IBinaryColumnClassifier
{
    IReadOnlyDictionary<string, LegacyBinaryColumnKind> Classify(
        LegacyTableSchema schema,
        ILegacyDataSource? dataSource);
}
