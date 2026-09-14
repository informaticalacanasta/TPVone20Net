using System.Collections;
using System.Data;
using System.Data.Common;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Data;

public sealed class CsvLegacyDataReader : DbDataReader
{
    private readonly PipeDelimitedRecordReader _reader;
    private readonly LegacyTableSchema _schema;
    private readonly LegacyValueConverter _converter;
    private readonly int _fieldCount;
    private object[]? _values;
    private bool _closed;

    public CsvLegacyDataReader(
        PipeDelimitedRecordReader reader,
        LegacyTableSchema schema,
        LegacyValueConverter converter)
    {
        _reader = reader;
        _schema = schema;
        _converter = converter;
        _fieldCount = schema.Columns.Count;
        var header = reader.ReadHeader()
            ?? throw new InvalidDataException("El CSV no contiene cabecera.");
        CsvHeaderValidator.EnsureMatches(header, schema);
    }

    public override int FieldCount => _fieldCount;

    public override bool HasRows => true;

    public override bool IsClosed => _closed;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        var record = _reader.ReadRecord(_fieldCount);
        if (record is null)
        {
            _values = null;
            return false;
        }

        _values = new object[_fieldCount];
        for (var index = 0; index < _fieldCount; index++)
        {
            _values[index] = _converter.ConvertValue(record[index], _schema.Columns[index]);
        }

        return true;
    }

    public override string GetName(int ordinal) => _schema.Columns[ordinal].Name;

    public override int GetOrdinal(string name)
    {
        for (var index = 0; index < _fieldCount; index++)
        {
            if (string.Equals(_schema.Columns[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new IndexOutOfRangeException($"No existe la columna '{name}'.");
    }

    public override object GetValue(int ordinal)
    {
        EnsureRow();
        return _values![ordinal];
    }

    public override bool IsDBNull(int ordinal)
    {
        return GetValue(ordinal) is DBNull;
    }

    public override Type GetFieldType(int ordinal)
    {
        return _converter.GetClrType(_schema.Columns[ordinal]);
    }

    public override string GetDataTypeName(int ordinal)
    {
        return _schema.Columns[ordinal].SourceTypeName;
    }

    public override int GetValues(object[] values)
    {
        EnsureRow();
        var count = Math.Min(values.Length, _fieldCount);
        Array.Copy(_values!, values, count);
        return count;
    }

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));
    public override string GetString(int ordinal) => Convert.ToString(GetValue(ordinal)) ?? string.Empty;

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var data = (byte[])GetValue(ordinal);
        if (buffer is null)
        {
            return data.Length;
        }

        var count = Math.Min(length, data.Length - (int)dataOffset);
        Array.Copy(data, (int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var data = GetString(ordinal);
        if (buffer is null)
        {
            return data.Length;
        }

        var count = Math.Min(length, data.Length - (int)dataOffset);
        data.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    public override DataTable GetSchemaTable()
    {
        var table = new DataTable();
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("AllowDBNull", typeof(bool));
        for (var index = 0; index < _fieldCount; index++)
        {
            var row = table.NewRow();
            row["ColumnName"] = _schema.Columns[index].Name;
            row["ColumnOrdinal"] = index;
            row["DataType"] = GetFieldType(index);
            row["AllowDBNull"] = _schema.Columns[index].IsNullable;
            table.Rows.Add(row);
        }

        return table;
    }

    public override void Close()
    {
        if (_closed)
        {
            return;
        }

        _reader.Dispose();
        _closed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    private void EnsureRow()
    {
        if (_values is null)
        {
            throw new InvalidOperationException("No hay un registro actual.");
        }
    }
}
