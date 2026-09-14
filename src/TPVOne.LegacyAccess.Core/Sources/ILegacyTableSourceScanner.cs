using TPVOne.LegacyAccess.Core.Models;

namespace TPVOne.LegacyAccess.Core.Sources;

public interface ILegacyTableSourceScanner
{
    LegacyDiscoveryResult Discover(string sourceDirectory);
}
