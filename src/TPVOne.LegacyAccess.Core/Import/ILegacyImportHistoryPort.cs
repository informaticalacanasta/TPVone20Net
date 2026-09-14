using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Import;

public interface ILegacyImportHistoryPort
{
    Task<bool> WasDataImportedAsync(
        string tableName,
        string dataHash,
        CancellationToken cancellationToken);

    Task<long> RecordStartAsync(
        TableImportResult draft,
        CancellationToken cancellationToken);

    Task RecordFinishAsync(
        long id,
        TableImportResult result,
        CancellationToken cancellationToken);
}
