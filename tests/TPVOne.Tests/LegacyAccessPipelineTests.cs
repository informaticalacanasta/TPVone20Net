using TPVOne.LegacyAccess.Core.Conversion;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Planning;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.Tests;

public sealed class LegacyAccessPipelineTests
{
    [Fact]
    public void ConversionPolicy_SameHashAndDestination_Skips()
    {
        var action = ConversionDeduplicationPolicy.Decide(
            AccessJetFormat.Jet3Access97,
            "AAA",
            destinationExists: true,
            successfulHistorySourceHash: "AAA");

        Assert.Equal(ConversionAction.SkipAlreadyConverted, action);
    }

    [Fact]
    public void ConversionPolicy_SameHashButIncompleteDestination_Reconverts()
    {
        var action = ConversionDeduplicationPolicy.Decide(
            AccessJetFormat.Jet3Access97,
            "AAA",
            destinationExists: true,
            successfulHistorySourceHash: "AAA",
            destinationIsCompleteAccessProject: false);

        Assert.Equal(ConversionAction.Reconvert, action);
    }

    [Fact]
    public void ConversionPolicy_DifferentHash_Reconverts()
    {
        var action = ConversionDeduplicationPolicy.Decide(
            AccessJetFormat.Jet3Access97,
            "BBB",
            destinationExists: true,
            successfulHistorySourceHash: "AAA");

        Assert.Equal(ConversionAction.Reconvert, action);
    }

    [Fact]
    public void ConversionPolicy_Jet4NewFile_Copies()
    {
        var action = ConversionDeduplicationPolicy.Decide(
            AccessJetFormat.Jet4Access2000,
            "AAA",
            destinationExists: false,
            successfulHistorySourceHash: null);

        Assert.Equal(ConversionAction.CopyCompatible, action);
    }

    [Fact]
    public void ImportPlan_SameHash_SkipsAutomatically()
    {
        var databases = new[] { Database("a.mdb", "AAA", Table("USUARIOS", 10)) };
        var plan = ImportPlanBuilder.Build(
            databases,
            [("AAA", "USUARIOS")],
            new Dictionary<string, long> { ["USUARIOS"] = 10 });

        Assert.Equal(PlannedTableStatus.SkipAlreadyImported, plan[0].Status);
    }

    [Fact]
    public void ImportPlan_MissingSqlTable_Creates()
    {
        var databases = new[] { Database("a.mdb", "AAA", Table("USUARIOS", 10)) };
        var plan = ImportPlanBuilder.Build(
            databases,
            [],
            new Dictionary<string, long>());

        Assert.Equal(PlannedTableStatus.Create, plan[0].Status);
    }

    [Fact]
    public void ImportPlan_ExistingTableDifferentHash_RequiresDecision()
    {
        var databases = new[] { Database("a.mdb", "BBB", Table("USUARIOS", 132)) };
        var plan = ImportPlanBuilder.Build(
            databases,
            [("AAA", "USUARIOS")],
            new Dictionary<string, long> { ["USUARIOS"] = 130 });

        Assert.Equal(PlannedTableStatus.RequiresDecision, plan[0].Status);
        Assert.Equal(130, plan[0].SqlRowCount);
        Assert.Equal(132, plan[0].SourceRowCount);
    }

    [Fact]
    public void ImportPlan_CollidingTableNames_AreNotMerged()
    {
        var databases = new[]
        {
            Database("a.mdb", "AAA", Table("USUARIOS", 10)),
            Database("b.mdb", "BBB", Table("USUARIOS", 11))
        };
        var plan = ImportPlanBuilder.Build(
            databases,
            [],
            new Dictionary<string, long>());

        Assert.All(plan, item => Assert.Equal(PlannedTableStatus.Collision, item.Status));
        Assert.Contains("a.mdb", plan[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b.mdb", plan[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertedPathResolver_KeepsRelativeSubdirectories()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "bds-root");
        var convertedRoot = Path.Combine(Path.GetTempPath(), "converted-root");
        var sourceFile = Path.Combine(sourceRoot, "tienda25", "usuarios.mdb");

        var destination = ConvertedPathResolver.GetConvertedPath(
            sourceRoot,
            sourceFile,
            convertedRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(convertedRoot, "tienda25", "usuarios.mdb")),
            destination);
    }

    [Fact]
    public void FileScanner_FindsNestedMdbFilesWithoutCollidingNames()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory.FullName, "tienda25"));
            Directory.CreateDirectory(Path.Combine(directory.FullName, "tienda66"));
            File.WriteAllBytes(Path.Combine(directory.FullName, "tienda25", "usuarios.mdb"), [1]);
            File.WriteAllBytes(Path.Combine(directory.FullName, "tienda66", "usuarios.mdb"), [2]);

            var files = LegacyFileScanner.FindMdbFiles(directory.FullName);

            Assert.Equal(2, files.Count);
            Assert.Contains(files, path => path.Contains("tienda25", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(files, path => path.Contains("tienda66", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void AccessFormatDetector_ReadsJet3AndJet4Headers()
    {
        var jet3 = Path.GetTempFileName();
        var jet4 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(jet3, Header("Standard Jet DB", 0));
            File.WriteAllBytes(jet4, Header("Standard Jet DB", 1));

            Assert.Equal(AccessJetFormat.Jet3Access97, AccessFormatDetector.Detect(jet3));
            Assert.Equal(AccessJetFormat.Jet4Access2000, AccessFormatDetector.Detect(jet4));
            Assert.True(AccessFormatDetector.RequiresJet4Conversion(AccessJetFormat.Jet3Access97));
            Assert.False(AccessFormatDetector.RequiresJet4Conversion(AccessJetFormat.Jet4Access2000));
        }
        finally
        {
            File.Delete(jet3);
            File.Delete(jet4);
        }
    }

    [Fact]
    public void Deduplication_SkipsOnlyPreviousSuccess()
    {
        Assert.True(ImportDeduplicationPolicy.ShouldSkipAlreadyImported(true));
        Assert.False(ImportDeduplicationPolicy.ShouldSkipAlreadyImported(false));
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

    [Fact]
    public void RowCountValidator_RequiresExactMatch()
    {
        Assert.True(RowCountValidator.Matches(132, 132));
        Assert.False(RowCountValidator.Matches(100, 99));
    }

    private static AccessDatabaseSchema Database(
        string file,
        string hash,
        params AccessTableSchema[] tables)
    {
        return new(file, hash, "Microsoft.Jet.OLEDB.4.0", tables, [], file);
    }

    private static AccessTableSchema Table(string name, long rows)
    {
        return new(name, [], [], rows);
    }

    private static byte[] Header(string magic, byte engine)
    {
        var bytes = new byte[24];
        System.Text.Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 4);
        bytes[0x14] = engine;
        return bytes;
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
            AccessTableSchema schema,
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
            AccessTableSchema schema,
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
