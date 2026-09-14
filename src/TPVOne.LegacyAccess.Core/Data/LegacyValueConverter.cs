using System.Globalization;
using TPVOne.LegacyAccess.Core.Binary;
using TPVOne.LegacyAccess.Core.Classification;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Data;

public sealed class LegacyValueConverter
{
    public object ConvertValue(string? raw, LegacyColumnSchema column)
    {
        if (raw is null || raw.Length == 0)
        {
            return DBNull.Value;
        }

        var type = column.SourceTypeName.Trim().ToUpperInvariant();
        try
        {
            return type switch
            {
                "DBBYTE" => byte.Parse(raw.Trim(), CultureInfo.InvariantCulture),
                "DBINTEGER" => short.Parse(raw.Trim(), CultureInfo.InvariantCulture),
                "DBLONG" => int.Parse(raw.Trim(), CultureInfo.InvariantCulture),
                "DBSINGLE" => float.Parse(raw.Trim(), CultureInfo.InvariantCulture),
                "DBDOUBLE" => double.Parse(raw.Trim(), CultureInfo.InvariantCulture),
                "DBCURRENCY" or "DBDECIMAL" or "DBNUMERIC" =>
                    decimal.Parse(raw.Trim(), CultureInfo.InvariantCulture),
                "DBBOOLEAN" => ParseBoolean(raw),
                "DBDATE" => ParseDate(raw),
                "DBTEXT" => raw,
                "DBMEMO" => DecodeMemoText(raw),
                "DBBINARY" or "DBLONGBINARY" => DecodeBinary(raw),
                "DBGUID" => Guid.Parse(raw.Trim()),
                _ => throw new DataImportException(
                    $"No hay conversión para el tipo DAO '{column.SourceTypeName}'.")
            };
        }
        catch (Exception exception) when (exception is not DataImportException)
        {
            throw new DataImportException(
                $"No se pudo convertir '{column.Name}' ({column.SourceTypeName}) desde '{Preview(raw)}'.",
                exception);
        }
    }

    public Type GetClrType(LegacyColumnSchema column)
    {
        return column.SourceTypeName.Trim().ToUpperInvariant() switch
        {
            "DBBYTE" => typeof(byte),
            "DBINTEGER" => typeof(short),
            "DBLONG" => typeof(int),
            "DBSINGLE" => typeof(float),
            "DBDOUBLE" => typeof(double),
            "DBCURRENCY" or "DBDECIMAL" or "DBNUMERIC" => typeof(decimal),
            "DBBOOLEAN" => typeof(bool),
            "DBDATE" => typeof(DateTime),
            "DBTEXT" or "DBMEMO" => typeof(string),
            "DBBINARY" or "DBLONGBINARY" => typeof(byte[]),
            "DBGUID" => typeof(Guid),
            _ => typeof(object)
        };
    }

    private static bool ParseBoolean(string raw)
    {
        var value = raw.Trim();
        if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value is "1" or "-1" ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new FormatException($"Valor bit no reconocido: '{value}'.");
    }

    private static DateTime ParseDate(string raw)
    {
        var value = raw.Trim();
        string[] formats =
        [
            "yyyy-MM-ddTHH:mm:ss.fffffff",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy"
        ];

        if (DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.NoCurrentDateDefault,
                out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Fecha no reconocida: '{value}'.");
    }

    private static string DecodeMemoText(string raw)
    {
        return Utf16LeTextCodec.DecodeBase64(raw);
    }

    private static byte[] DecodeBinary(string raw)
    {
        var decoded = Base64Decoder.Decode(raw);
        return LegacyBinaryPayloadInterpreter.Interpret(decoded);
    }

    private static string Preview(string raw)
    {
        var compact = raw.Length <= 40 ? raw : raw[..40] + "...";
        return compact.Replace('\n', ' ').Replace('\r', ' ');
    }
}
