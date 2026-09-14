using System.Data.OleDb;
using System.Text;
using TPVOne.LegacyAccess.Core.Conversion;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Planning;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.Tests;

public sealed class LegacyAccessCoreTests
{
    private readonly AccessToSqlTypeMapper _mapper = new();

    [Theory]
    [InlineData(OleDbType.VarWChar, 25, "nvarchar(25)")]
    [InlineData(OleDbType.LongVarWChar, null, "nvarchar(max)")]
    [InlineData(OleDbType.Double, null, "float")]
    [InlineData(OleDbType.Currency, null, "decimal(19,4)")]
    [InlineData(OleDbType.Boolean, null, "bit")]
    [InlineData(OleDbType.Guid, null, "uniqueidentifier")]
    [InlineData(OleDbType.LongVarBinary, null, "varbinary(max)")]
    public void TypeMapper_MapsProviderTypeFaithfully(
        OleDbType providerType,
        int? length,
        string expected)
    {
        var column = Column("Value", providerType, length);

        Assert.Equal(expected, _mapper.Map(column).ToSql());
    }

    [Fact]
    public void QuoteIdentifier_EscapesClosingBracket()
    {
        Assert.Equal("[VENTAS 2024]]final]", SqlIdentifier.Quote("VENTAS 2024]final"));
    }

    [Theory]
    [InlineData("MSysObjects", false)]
    [InlineData("msysRelationships", false)]
    [InlineData("STOCK", true)]
    public void AccessObjectFilter_ExcludesSystemTables(string name, bool expected)
    {
        Assert.Equal(expected, AccessObjectFilter.IsUserTable(name));
    }

    [Theory]
    [InlineData("MSysAccessObjects", true)]
    [InlineData("MSysAccessStorage", true)]
    [InlineData("MSysObjects", false)]
    public void AccessFormatDetector_DetectsAccessApplicationCatalog(
        string catalogTable,
        bool expected)
    {
        Assert.Equal(
            expected,
            AccessFormatDetector.HasAccessApplicationCatalog(["usuarios", catalogTable]));
    }

    [Fact]
    public void FileScanner_SortsMdbFilesDeterministically()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "zeta.mdb"), []);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Alfa.mdb"), []);
            File.WriteAllBytes(Path.Combine(directory.FullName, "ignore.accdb"), []);

            var files = LegacyFileScanner.FindMdbFiles(directory.FullName);

            Assert.Equal(["Alfa.mdb", "zeta.mdb"], files.Select(Path.GetFileName));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Sha256_IsCalculatedFromFileContents()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc", Encoding.ASCII);

            var hash = await FileHashCalculator.CalculateSha256Async(path);

            Assert.Equal(
                "BA7816BF8F01CFEA414140DE5DAE2223" +
                "B00361A396177A9CB410FF61F20015AD",
                hash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Deduplication_OnlySkipsSuccessfulHash(
        bool previousSuccess,
        bool expected)
    {
        Assert.Equal(
            expected,
            ImportDeduplicationPolicy.ShouldSkipAlreadyImported(previousSuccess));
    }

    [Fact]
    public void SchemaComparison_BlocksIncompatibleType()
    {
        var access = new AccessTableSchema(
            "T",
            [Column("PRICE", OleDbType.Double)],
            [],
            0);
        var sql = new SqlTableSchema(
            "T",
            [new("PRICE", new("int"), true, false, 0)],
            []);

        var differences = new SchemaComparisonService(_mapper).Compare(access, sql);

        Assert.Contains(
            differences,
            difference =>
                difference.Kind == SchemaDifferenceKind.TypeMismatch &&
                difference.BlocksImport);
    }

    [Fact]
    public void SchemaComparison_AllowsLargerTextDestination()
    {
        var access = new AccessTableSchema(
            "T",
            [Column("NAME", OleDbType.VarWChar, 20)],
            [],
            0);
        var sql = new SqlTableSchema(
            "T",
            [new("NAME", new("nvarchar", 50), true, false, 0)],
            []);

        var differences = new SchemaComparisonService(_mapper).Compare(access, sql);

        Assert.DoesNotContain(differences, difference => difference.BlocksImport);
        Assert.Contains(
            differences,
            difference => difference.Kind == SchemaDifferenceKind.Compatible);
    }

    private static AccessColumnSchema Column(
        string name,
        OleDbType providerType,
        int? length = null)
    {
        return new(
            name,
            providerType,
            typeof(object).FullName!,
            length,
            null,
            null,
            true,
            false,
            0,
            null);
    }
}
