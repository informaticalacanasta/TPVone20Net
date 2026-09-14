namespace TPVOne.LegacyAccess.Core.Models;

public sealed record ProviderAttempt(
    string Provider,
    string Status,
    string? Detail);

public sealed record AccessProviderSelection(
    string Provider,
    string ConnectionString,
    IReadOnlyList<ProviderAttempt> Attempts);
