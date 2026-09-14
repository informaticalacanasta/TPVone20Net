using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Sources;

namespace TPVOne.LegacyAccess.Core.Import;

public interface ILegacyDataCopyPort
{
    Task<long> CopyAsync(
        LegacyTableSchema schema,
        ILegacyDataSource dataSource,
        string destinationTableName,
        long expectedRowCount,
        CancellationToken cancellationToken);
}
