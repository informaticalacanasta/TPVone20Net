using TPVOne.LegacyAccess.Core.Import;

namespace TPVOne.LegacyAccessImporter.Import;

internal sealed class ConsoleLegacyImportInteraction : ILegacyImportInteraction
{
    private readonly bool _overwriteAll;

    public ConsoleLegacyImportInteraction(bool overwriteAll = false)
    {
        _overwriteAll = overwriteAll;
    }

    public void Inform(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush();
    }

    public bool ConfirmOverwrite(string tableName)
    {
        if (_overwriteAll)
        {
            Inform($"La tabla '{tableName}' ya existe. Sobrescribiendo.");
            return true;
        }

        if (Console.IsOutputRedirected)
        {
            return ConfirmThroughParent(tableName);
        }

        return ConfirmWithReadKey(tableName);
    }

    private static bool ConfirmThroughParent(string tableName)
    {
        Console.WriteLine(OverwriteConfirmationProtocol.RequestLine(tableName));
        Console.Out.Flush();
        while (true)
        {
            var line = Console.ReadLine();
            if (line is null)
            {
                return false;
            }

            foreach (var key in line)
            {
                var parsed = OverwriteDecision.Parse(key);
                if (parsed is not null)
                {
                    return parsed.Value;
                }
            }
        }
    }

    private static bool ConfirmWithReadKey(string tableName)
    {
        Console.Write(OverwriteConfirmationProtocol.Prompt(tableName));
        Console.Out.Flush();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var parsed = OverwriteDecision.Parse(key.KeyChar);
            if (parsed is null)
            {
                continue;
            }

            Console.WriteLine(parsed.Value ? "S" : "N");
            Console.Out.Flush();
            return parsed.Value;
        }
    }
}
