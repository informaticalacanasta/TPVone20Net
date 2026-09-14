using TPVOne.LegacyAccess.Core.Data;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Parsing;

namespace TPVOne.LegacyAccess.Core.Sources;

public sealed class TxtLegacySchemaSource : ILegacySchemaSource
{
    private readonly TxtTableStructureParser _parser;

    public TxtLegacySchemaSource(string location, TxtTableStructureParser? parser = null)
    {
        Location = location;
        _parser = parser ?? new TxtTableStructureParser();
    }

    public string Location { get; }

    public async Task<LegacyTableSchema> ReadSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        var encoding = TextEncodingDetector.Resolve(Location, "windows-1252");
        var content = await File.ReadAllTextAsync(Location, encoding, cancellationToken);
        var schema = _parser.Parse(content);
        LegacyLogicalNameValidator.EnsureMatchesFile(Location, schema.Name);
        return schema;
    }
}
