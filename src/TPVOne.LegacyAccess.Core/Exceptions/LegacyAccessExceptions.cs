namespace TPVOne.LegacyAccess.Core.Exceptions;

public class LegacyAccessException : Exception
{
    public LegacyAccessException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class SchemaExtractionException : LegacyAccessException
{
    public SchemaExtractionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class UnsupportedDaoTypeException : LegacyAccessException
{
    public UnsupportedDaoTypeException(string message)
        : base(message)
    {
    }
}

public sealed class SchemaNameMismatchException : LegacyAccessException
{
    public SchemaNameMismatchException(string message)
        : base(message)
    {
    }
}

public sealed class OrphanDataSourceException : LegacyAccessException
{
    public OrphanDataSourceException(string message)
        : base(message)
    {
    }
}

public sealed class SqlSchemaConflictException : LegacyAccessException
{
    public SqlSchemaConflictException(string message)
        : base(message)
    {
    }
}

public sealed class DataImportException : LegacyAccessException
{
    public DataImportException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class CsvHeaderMismatchException : LegacyAccessException
{
    public CsvHeaderMismatchException(string message)
        : base(message)
    {
    }
}
