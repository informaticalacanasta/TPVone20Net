namespace TPVOne.LegacyAccess.Core.Import;

public static class OverwriteDecision
{
    public static bool? Parse(char key)
    {
        return key switch
        {
            'S' or 's' => true,
            'N' or 'n' => false,
            _ => null
        };
    }

    public static bool FromCharacters(IEnumerable<char> keys)
    {
        foreach (var key in keys)
        {
            var parsed = Parse(key);
            if (parsed is not null)
            {
                return parsed.Value;
            }
        }

        throw new InvalidOperationException("No se recibió S o N.");
    }
}
