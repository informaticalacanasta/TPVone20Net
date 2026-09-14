using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Planning;

namespace TPVOne.Tests;

public sealed class LegacyAccessPipelineTests
{
    [Fact]
    public void Deduplication_SkipsOnlyPreviousSuccess()
    {
        Assert.True(ImportDeduplicationPolicy.ShouldSkipAlreadyImported(true));
        Assert.False(ImportDeduplicationPolicy.ShouldSkipAlreadyImported(false));
        Assert.False(ImportDeduplicationPolicy.ShouldSkipAlreadyImported(true, forceImport: true));
    }

    [Fact]
    public void RowCountValidator_RequiresExactMatch()
    {
        Assert.True(RowCountValidator.Matches(132, 132));
        Assert.False(RowCountValidator.Matches(100, 99));
    }

    [Fact]
    public async Task SafeReplacement_ReplaceLeavesOriginalWhenSwapFails()
    {
        var schema = Table("USUARIOS", 120);
        var ops = new FakeReplacementOperations("USUARIOS", 100);
        ops.ThrowOnSwap = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SafeReplacementWorkflow().ReplaceOrCreateAsync(
                ops,
                schema,
                "USUARIOS",
                "__stg",
                "__old"));

        Assert.Equal(100, ops.Tables["USUARIOS"]);
        Assert.False(ops.Tables.ContainsKey("__stg"));
    }

    [Fact]
    public async Task SafeReplacement_DoesNotSwapWhenRowCountMismatches()
    {
        var schema = Table("USUARIOS", 100);
        var ops = new FakeReplacementOperations("USUARIOS", 100)
        {
            StagingImportCount = 99
        };

        await Assert.ThrowsAsync<DataImportException>(() =>
            new SafeReplacementWorkflow().ReplaceOrCreateAsync(
                ops,
                schema,
                "USUARIOS",
                "__stg",
                "__old"));

        Assert.Equal(100, ops.Tables["USUARIOS"]);
        Assert.False(ops.Tables.ContainsKey("__stg"));
    }

    [Fact]
    public async Task SafeReplacement_ReplaceSwapsCompleteNewContents()
    {
        var schema = Table("USUARIOS", 120);
        var ops = new FakeReplacementOperations("USUARIOS", 100)
        {
            StagingImportCount = 120
        };

        var imported = await new SafeReplacementWorkflow().ReplaceOrCreateAsync(
            ops,
            schema,
            "USUARIOS",
            "__stg",
            "__old");

        Assert.Equal(120, imported);
        Assert.Equal(120, ops.Tables["USUARIOS"]);
        Assert.False(ops.Tables.ContainsKey("__old"));
        Assert.False(ops.Tables.ContainsKey("__stg"));
    }

    private static LegacyTableSchema Table(string name, long rows)
    {
        return new(name, [], [], rows);
    }

    private sealed class FakeReplacementOperations : IReplacementOperations
    {
        public Dictionary<string, long> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowOnSwap { get; set; }
        public long StagingImportCount { get; set; } = 120;

        public FakeReplacementOperations(string existingName, long existingRows)
        {
            Tables[existingName] = existingRows;
        }

        public Task CreateEmptyTableAsync(
            string tableName,
            LegacyTableSchema schema,
            CancellationToken cancellationToken)
        {
            Tables[tableName] = 0;
            return Task.CompletedTask;
        }

        public Task<long> CopyDataAsync(string tableName, CancellationToken cancellationToken)
        {
            Tables[tableName] = StagingImportCount;
            return Task.FromResult(StagingImportCount);
        }

        public Task CreateIndexesAsync(
            string tableName,
            LegacyTableSchema schema,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<long> CountAsync(string tableName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Tables[tableName]);
        }

        public Task<bool> ExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Tables.ContainsKey(tableName));
        }

        public Task SwapAtomicAsync(
            string destinationTableName,
            string stagingTableName,
            string? backupTableName,
            CancellationToken cancellationToken)
        {
            if (ThrowOnSwap)
            {
                throw new InvalidOperationException("swap failed");
            }

            if (backupTableName is not null)
            {
                Tables[backupTableName] = Tables[destinationTableName];
                Tables.Remove(destinationTableName);
            }

            Tables[destinationTableName] = Tables[stagingTableName];
            Tables.Remove(stagingTableName);
            return Task.CompletedTask;
        }

        public Task DropIfExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            Tables.Remove(tableName);
            return Task.CompletedTask;
        }
    }
}
