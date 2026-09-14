using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Planning;

public static class CollisionDetector
{
    public static IReadOnlySet<string> FindTableNameCollisions(
        IEnumerable<AccessDatabaseSchema> databases)
    {
        return databases
            .SelectMany(database => database.Tables.Select(table => table.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> DescribeCollisions(
        IEnumerable<AccessDatabaseSchema> databases,
        string tableName)
    {
        return databases
            .Where(database => database.Tables.Any(table =>
                string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase)))
            .Select(database => database.OriginalFilePath ?? database.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class ImportDeduplicationPolicy
{
    public static bool ShouldSkipAlreadyImported(bool previousSuccessfulImport)
    {
        return previousSuccessfulImport;
    }
}

public static class RowCountValidator
{
    public static bool Matches(long sourceRows, long destinationRows)
    {
        return sourceRows == destinationRows;
    }
}

public static class ImportPlanBuilder
{
    public static IReadOnlyList<PlannedTable> Build(
        IReadOnlyList<AccessDatabaseSchema> databases,
        IReadOnlyCollection<(string Hash, string Table)> successfulImports,
        IReadOnlyDictionary<string, long> existingSqlRowCounts)
    {
        var collisions = CollisionDetector.FindTableNameCollisions(databases);
        var plan = new List<PlannedTable>();

        foreach (var database in databases)
        {
            foreach (var table in database.Tables)
            {
                var original = database.OriginalFilePath ?? database.FilePath;
                if (collisions.Contains(table.Name))
                {
                    var sources = CollisionDetector.DescribeCollisions(databases, table.Name);
                    plan.Add(new(
                        original,
                        database.FilePath,
                        database.SourceHash,
                        table.Name,
                        table.RowCount,
                        existingSqlRowCounts.ContainsKey(table.Name),
                        existingSqlRowCounts.GetValueOrDefault(table.Name),
                        PlannedTableStatus.Collision,
                        "La tabla aparece en más de un MDB: " +
                        string.Join("; ", sources) +
                        ". No se mezclan datos automáticamente."));
                    continue;
                }

                if (successfulImports.Any(item =>
                    string.Equals(item.Hash, database.SourceHash, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Table, table.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Add(new(
                        original,
                        database.FilePath,
                        database.SourceHash,
                        table.Name,
                        table.RowCount,
                        existingSqlRowCounts.ContainsKey(table.Name),
                        existingSqlRowCounts.GetValueOrDefault(table.Name),
                        PlannedTableStatus.SkipAlreadyImported,
                        "Este origen ya fue importado correctamente. Hash coincidente."));
                    continue;
                }

                if (!existingSqlRowCounts.ContainsKey(table.Name))
                {
                    plan.Add(new(
                        original,
                        database.FilePath,
                        database.SourceHash,
                        table.Name,
                        table.RowCount,
                        false,
                        null,
                        PlannedTableStatus.Create,
                        "La tabla SQL no existe. Se creará e importará."));
                    continue;
                }

                plan.Add(new(
                    original,
                    database.FilePath,
                    database.SourceHash,
                    table.Name,
                    table.RowCount,
                    true,
                    existingSqlRowCounts[table.Name],
                    PlannedTableStatus.RequiresDecision,
                    "La tabla SQL ya existe y el origen es distinto al importado anteriormente."));
            }
        }

        return plan;
    }
}
