using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.Tests;

public sealed class LegacyAccessCoreTests
{
    private readonly DaoToSqlTypeMapper _mapper = new();

    [Fact]
    public void QuoteIdentifier_EscapesClosingBracket()
    {
        Assert.Equal("[VENTAS 2024]]final]", SqlIdentifier.Quote("VENTAS 2024]final"));
    }

    [Fact]
    public void SchemaComparison_BlocksIncompatibleType()
    {
        var source = new LegacyTableSchema(
            "T",
            [Column("PRICE", "dbDouble")],
            []);
        var sql = new SqlTableSchema(
            "T",
            [new("PRICE", new("int"), true, false, 0)],
            []);

        var differences = new SchemaComparisonService(_mapper).Compare(source, sql);

        Assert.Contains(
            differences,
            difference =>
                difference.Kind == SchemaDifferenceKind.TypeMismatch &&
                difference.BlocksImport);
    }

    [Fact]
    public void SchemaComparison_AllowsLargerTextDestination()
    {
        var source = new LegacyTableSchema(
            "T",
            [Column("NAME", "dbText", 20)],
            []);
        var sql = new SqlTableSchema(
            "T",
            [new("NAME", new("nvarchar", 50), true, false, 0)],
            []);

        var differences = new SchemaComparisonService(_mapper).Compare(source, sql);

        Assert.DoesNotContain(differences, difference => difference.BlocksImport);
        Assert.Contains(
            differences,
            difference => difference.Kind == SchemaDifferenceKind.Compatible);
    }

    internal static LegacyColumnSchema Column(
        string name,
        string sourceType,
        int? size = null)
    {
        return new(name, sourceType, size, null, null, 0, true, false);
    }
}
