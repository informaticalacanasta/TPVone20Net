using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Planning;

public static class CollisionDetector
{
    public static IReadOnlySet<string> FindTableNameCollisions(
        IEnumerable<string> tableNames)
    {
        return tableNames
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public static class ImportDeduplicationPolicy
{
    public static bool ShouldSkipAlreadyImported(
        bool previousSuccessfulImport,
        bool forceImport = false)
    {
        return previousSuccessfulImport && !forceImport;
    }
}

public static class RowCountValidator
{
    public static bool Matches(long sourceRows, long destinationRows)
    {
        return sourceRows == destinationRows;
    }
}
