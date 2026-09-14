namespace TPVOne.LegacyAccess.Core.Conversion;

public interface ILegacyAccessConverter
{
    void ConvertToJet4(string sourceFile, string destinationFile, string? password = null);
}
