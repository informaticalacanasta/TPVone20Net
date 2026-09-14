using System.Text.Json;
using System.Text.Json.Serialization;

namespace TPVOne.LegacyAccess.Core.Models;

public static class PipelineJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class DecisionFile
{
    public List<TableDecision> Items { get; set; } = [];
}
