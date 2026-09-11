IF OBJECT_ID(N'dbo.LegacyImportHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LegacyImportHistory
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_LegacyImportHistory PRIMARY KEY,
        SourceFile nvarchar(1000) NOT NULL,
        SourceFileName nvarchar(260) NOT NULL,
        SourceFileSize bigint NULL,
        SourceLastWriteTime datetime2 NULL,
        SourceHash varchar(64) NOT NULL,
        TableName nvarchar(255) NOT NULL,
        SourceRowCount bigint NULL,
        ImportedRowCount bigint NULL,
        StartedAt datetime2 NOT NULL,
        FinishedAt datetime2 NULL,
        Status nvarchar(50) NOT NULL,
        ErrorMessage nvarchar(max) NULL
    );

    CREATE INDEX IX_LegacyImportHistory_SourceHash_TableName_Status
        ON dbo.LegacyImportHistory (SourceHash, TableName, Status);
END;
GO
