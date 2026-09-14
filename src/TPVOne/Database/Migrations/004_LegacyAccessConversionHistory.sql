IF OBJECT_ID(N'dbo.LegacyAccessConversionHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegacyAccessConversionHistory
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_LegacyAccessConversionHistory PRIMARY KEY,
        SourceFile nvarchar(1000) NOT NULL,
        SourceFileName nvarchar(260) NOT NULL,
        SourceFileSize bigint NULL,
        SourceLastWriteTime datetime2 NULL,
        SourceHash varchar(64) NOT NULL,
        ConvertedFile nvarchar(1000) NULL,
        ConvertedFileSize bigint NULL,
        ConvertedHash varchar(64) NULL,
        SourceFormat nvarchar(50) NULL,
        Action nvarchar(50) NOT NULL,
        StartedAt datetime2 NOT NULL,
        FinishedAt datetime2 NULL,
        Status nvarchar(50) NOT NULL,
        ErrorMessage nvarchar(max) NULL
    );

    CREATE INDEX IX_LegacyAccessConversionHistory_SourceHash_Status
        ON dbo.LegacyAccessConversionHistory (SourceHash, Status);
END;
GO
