IF COL_LENGTH(N'dbo.LegacyImportHistory', N'Action') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD Action nvarchar(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'PreviousRowCount') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD PreviousRowCount bigint NULL;
END;
GO

IF COL_LENGTH(N'dbo.LegacyImportHistory', N'ReplacementOfImportId') IS NULL
BEGIN
    ALTER TABLE dbo.LegacyImportHistory
        ADD ReplacementOfImportId bigint NULL;
END;
GO
