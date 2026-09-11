using System.Data.OleDb;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Schema;

public sealed class AccessToSqlTypeMapper
{
    public SqlTypeDefinition Map(AccessColumnSchema column)
    {
        return column.ProviderType switch
        {
            OleDbType.Char or OleDbType.VarChar or
            OleDbType.WChar or OleDbType.VarWChar =>
                new("nvarchar", NormalizeTextLength(column.MaxLength)),
            OleDbType.LongVarChar or OleDbType.LongVarWChar =>
                new("nvarchar", -1),
            OleDbType.UnsignedTinyInt => new("tinyint"),
            OleDbType.TinyInt or OleDbType.SmallInt => new("smallint"),
            OleDbType.Integer => new("int"),
            OleDbType.BigInt or OleDbType.UnsignedBigInt => new("bigint"),
            OleDbType.UnsignedSmallInt => new("int"),
            OleDbType.UnsignedInt => new("bigint"),
            OleDbType.Single => new("real"),
            OleDbType.Double => new("float"),
            OleDbType.Decimal or OleDbType.Numeric or OleDbType.VarNumeric =>
                new(
                    "decimal",
                    Precision: NormalizePrecision(column.Precision),
                    Scale: NormalizeScale(column.Precision, column.Scale)),
            OleDbType.Currency => new("decimal", Precision: 19, Scale: 4),
            OleDbType.Date or OleDbType.DBDate or OleDbType.DBTime or
            OleDbType.DBTimeStamp => new("datetime2"),
            OleDbType.Boolean => new("bit"),
            OleDbType.Binary or OleDbType.VarBinary or OleDbType.LongVarBinary =>
                new("varbinary", -1),
            OleDbType.Guid => new("uniqueidentifier"),
            _ => throw new UnsupportedAccessTypeException(
                $"La columna '{column.Name}' usa el tipo OLE DB " +
                $"no soportado '{column.ProviderType}' ({(int)column.ProviderType}).")
        };
    }

    private static int NormalizeTextLength(int? length)
    {
        return length is > 0 and <= 4000 ? length.Value : -1;
    }

    private static byte NormalizePrecision(byte? precision)
    {
        return precision is > 0 and <= 38 ? precision.Value : (byte)18;
    }

    private static byte NormalizeScale(byte? precision, byte? scale)
    {
        var normalizedPrecision = NormalizePrecision(precision);
        return scale is not null && scale <= normalizedPrecision
            ? scale.Value
            : (byte)0;
    }
}
