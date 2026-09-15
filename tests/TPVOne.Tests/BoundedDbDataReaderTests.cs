using System.Data.Common;
using TPVOne.LegacyAccess.Core.Data;
using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.Tests;

public sealed class BoundedDbDataReaderTests
{
    [Fact]
    public void ReadsAtMostMaxRows_ThenInnerContinues()
    {
        using var inner = new SequenceReader(6);
        using var first = new BoundedDbDataReader(inner, 2);
        Assert.True(first.Read());
        Assert.True(first.Read());
        Assert.False(first.Read());
        Assert.Equal(2, first.RowsReturned);
        Assert.False(first.SourceExhausted);

        using var second = new BoundedDbDataReader(inner, 2);
        Assert.True(second.Read());
        Assert.True(second.Read());
        Assert.False(second.Read());
        Assert.False(second.SourceExhausted);

        using var third = new BoundedDbDataReader(inner, 2);
        Assert.True(third.Read());
        Assert.True(third.Read());
        Assert.False(third.Read());
        Assert.Equal(2, third.RowsReturned);
        Assert.False(third.SourceExhausted);

        using var leftover = new BoundedDbDataReader(inner, 2);
        Assert.False(leftover.Read());
        Assert.True(leftover.SourceExhausted);
        Assert.Equal(0, leftover.RowsReturned);
    }

    [Fact]
    public void DisposeDoesNotCloseInner()
    {
        using var inner = new SequenceReader(1);
        var bounded = new BoundedDbDataReader(inner, 1);
        bounded.Dispose();
        Assert.False(inner.IsClosed);
        Assert.True(inner.Read());
    }
}

public sealed class StagingDdlTests
{
    [Fact]
    public void UniqueIndexNames_AreSuffixedForStaging()
    {
        var schema = new LegacyTableSchema(
            "articulos",
            [new("id", "dbLong", 4, null, null, 0, false, false)],
            [new("PK_articulos", true, true, [new("id", 0, false)])]);
        var sql = new LegacySqlDdlBuilder(new DaoToSqlTypeMapper())
            .BuildCreateIndexSql(schema, "articulos__sabc", "abc");

        Assert.Contains("PK_articulos__abc", sql[0], StringComparison.Ordinal);
        Assert.DoesNotContain("CONSTRAINT [PK_articulos] ", sql[0], StringComparison.Ordinal);
    }

    [Fact]
    public void StagingTableName_IncludesToken()
    {
        var name = LegacyStagingNames.StagingTable("articulos", "abc123");
        Assert.Equal("articulos__sabc123", name);
        Assert.NotEqual("articulos_staging", name);
    }
}

file sealed class SequenceReader : DbDataReader
{
    private readonly int _total;
    private int _current = -1;
    private bool _closed;

    public SequenceReader(int total)
    {
        _total = total;
    }

    public override int FieldCount => 1;

    public override bool HasRows => true;

    public override bool IsClosed => _closed;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => _current;

    public override object this[string name] => _current;

    public override bool Read()
    {
        if (_current + 1 >= _total)
        {
            return false;
        }

        _current++;
        return true;
    }

    public override string GetName(int ordinal) => "id";

    public override int GetOrdinal(string name) => 0;

    public override object GetValue(int ordinal) => _current;

    public override bool IsDBNull(int ordinal) => false;

    public override Type GetFieldType(int ordinal) => typeof(int);

    public override string GetDataTypeName(int ordinal) => "int";

    public override int GetValues(object[] values)
    {
        values[0] = _current;
        return 1;
    }

    public override bool GetBoolean(int ordinal) => false;

    public override byte GetByte(int ordinal) => (byte)_current;

    public override char GetChar(int ordinal) => (char)_current;

    public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;

    public override decimal GetDecimal(int ordinal) => _current;

    public override double GetDouble(int ordinal) => _current;

    public override float GetFloat(int ordinal) => _current;

    public override Guid GetGuid(int ordinal) => Guid.Empty;

    public override short GetInt16(int ordinal) => (short)_current;

    public override int GetInt32(int ordinal) => _current;

    public override long GetInt64(int ordinal) => _current;

    public override string GetString(int ordinal) => _current.ToString();

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => 0;

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => 0;

    public override bool NextResult() => false;

    public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();

    public override void Close() => _closed = true;
}
