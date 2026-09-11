namespace TPVOne.LegacyAccess.Core.Exceptions;

public class LegacyAccessException : Exception
{
    public LegacyAccessException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class AccessProviderNotInstalledException : LegacyAccessException
{
    public AccessProviderNotInstalledException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class UnsupportedAccessVersionException : LegacyAccessException
{
    public UnsupportedAccessVersionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class AccessDatabaseCorruptedException : LegacyAccessException
{
    public AccessDatabaseCorruptedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class AccessDatabasePasswordProtectedException : LegacyAccessException
{
    public AccessDatabasePasswordProtectedException(string message, Exception? inner = null)
        : base(message, inner)
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

public sealed class UnsupportedAccessTypeException : LegacyAccessException
{
    public UnsupportedAccessTypeException(string message)
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
