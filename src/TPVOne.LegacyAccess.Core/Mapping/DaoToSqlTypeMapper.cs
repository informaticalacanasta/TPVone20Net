using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Mapping;

public sealed class DaoToSqlTypeMapper
{
    public SqlTypeDefinition Map(LegacyColumnSchema column)
    {
        var sourceType = column.SourceTypeName.Trim();
        return sourceType.ToUpperInvariant() switch
        {
            "DBBYTE" => new("tinyint"),
            "DBINTEGER" => new("smallint"),
            "DBLONG" => new("int"),
            "DBSINGLE" => new("real"),
            "DBDOUBLE" => new("float"),
            "DBCURRENCY" => new("decimal", Precision: 19, Scale: 4),
            "DBDECIMAL" or "DBNUMERIC" => new(
                "decimal",
                Precision: NormalizePrecision(column.Precision),
                Scale: NormalizeScale(column.Precision, column.Scale)),
            "DBBOOLEAN" => new("bit"),
            "DBDATE" => new("datetime2"),
            "DBTEXT" => new("nvarchar", NormalizeTextLength(column.Size, requireSize: true, column.Name)),
            "DBMEMO" => new("nvarchar", -1),
            "DBBINARY" => new("varbinary", NormalizeBinaryLength(column.Size)),
            "DBLONGBINARY" => new("varbinary", -1),
            "DBGUID" => new("uniqueidentifier"),
            _ => throw new UnsupportedDaoTypeException(
                $"La columna '{column.Name}' usa el tipo DAO no soportado '{column.SourceTypeName}'.")
        };
    }

    private static int NormalizeTextLength(int? length, bool requireSize, string columnName)
    {
        if (length is null)
        {
            if (requireSize)
            {
                throw new UnsupportedDaoTypeException(
                    $"La columna '{columnName}' es dbText y no declara size.");
            }

            return -1;
        }

        return length is > 0 and <= 4000 ? length.Value : -1;
    }

    private static int NormalizeBinaryLength(int? length)
    {
        return length is > 0 and <= 8000 ? length.Value : -1;
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
