IF COL_LENGTH(N'dbo.LegacyImportHistory', N'StructureFile') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD StructureFile nvarchar(1000) NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'StructureHash') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD StructureHash varchar(64) NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'DataFile') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD DataFile nvarchar(1000) NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'DataHash') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD DataHash varchar(64) NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'SchemaStatus') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD SchemaStatus nvarchar(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'DataStatus') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD DataStatus nvarchar(50) NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LegacyImportHistory_DataHash_TableName'
      AND object_id = OBJECT_ID(N'dbo.LegacyImportHistory', N'U')
)
BEGIN
    CREATE INDEX IX_LegacyImportHistory_DataHash_TableName
        ON dbo.LegacyImportHistory (DataHash, TableName);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LegacyImportHistory_StructureHash_TableName'
      AND object_id = OBJECT_ID(N'dbo.LegacyImportHistory', N'U')
)
BEGIN
    CREATE INDEX IX_LegacyImportHistory_StructureHash_TableName
        ON dbo.LegacyImportHistory (StructureHash, TableName);
END;
GO
