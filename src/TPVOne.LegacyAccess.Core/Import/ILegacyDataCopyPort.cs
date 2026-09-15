using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Sources;

namespace TPVOne.LegacyAccess.Core.Import;

public sealed record LegacyCopyResult(long RowsCopied, int BatchCount);

public sealed record LegacyCopyProgress(long RowsCopied, long ExpectedRows, int BatchCount);

public interface ILegacyDataCopyPort
{
    Task<LegacyCopyResult> CopyAsync(
        LegacyTableSchema schema,
        ILegacyDataSource dataSource,
        string destinationTableName,
        long expectedRowCount,
        IProgress<LegacyCopyProgress>? progress,
        CancellationToken cancellationToken);
}
