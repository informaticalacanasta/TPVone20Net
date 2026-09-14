namespace TPVOne.LegacyAccess.Core.Import;

public interface ILegacyImportInteraction
{
    void Inform(string message);

    bool ConfirmOverwrite(string tableName);
}
