using TPVOne.LegacyAccess.Core.Sources;

namespace TPVOne.LegacyAccess.Core.Models;

public sealed record LegacyTableSource(
    string LogicalName,
    ILegacySchemaSource SchemaSource,
    ILegacyDataSource? DataSource);

public sealed record LegacySourceMetadata(
    string LogicalName,
    string SchemaLocation,
    string? DataLocation,
    string StructureHash,
    string? DataHash);

public sealed record LegacyDiscoveryResult(
    IReadOnlyList<LegacyTableSource> Sources,
    IReadOnlyList<string> OrphanDataFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
