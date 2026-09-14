using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Sources;

public interface ILegacySchemaSource
{
    string Location { get; }

    Task<LegacyTableSchema> ReadSchemaAsync(CancellationToken cancellationToken = default);
}
