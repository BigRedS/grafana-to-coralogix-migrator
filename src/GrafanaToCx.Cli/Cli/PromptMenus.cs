using GrafanaToCx.Core.Migration;
using Sharprompt;

namespace GrafanaToCx.Cli.Cli;

/// <summary>
/// Sharprompt-based main menu and settings menu.
/// </summary>
public static class PromptMenus
{
    private const string KnownRegions = "eu1, eu2, us1, us2, ap1, ap2, ap3, in1";

    public sealed record MainMenuItem(string Key, string Label)
    {
        public override string ToString() => $"{Key}. {Label}";
    }

    private static readonly MainMenuItem[] MainMenuItems =
    [
        new("1", "Convert – Grafana JSON → CX JSON (local)"),
        new("2", "Push – Push single dashboard to Coralogix"),
        new("3", "Import – Import folder of dashboards"),
        new("4", "Migrate – Bulk migrate from Grafana"),
        new("5", "Settings – Change connection settings"),
        new("6", "Cleanup – Backup and delete dashboards by folder"),
        new("0", "Exit")
    ];

    public static MainMenuItem? ShowMainMenu(string endpoint)
    {
        Console.WriteLine();
        Console.WriteLine("══ Main Menu ══════════════════════════════");
        Console.WriteLine($"   Endpoint: {endpoint}");
        Console.WriteLine("──────────────────────────────────────────");

        var selected = Prompt.Select("Choice", MainMenuItems, textSelector: x => x.ToString());
        return selected;
    }

    public static SessionConfig RunSettingsMenu(SessionConfig current)
    {
        Console.WriteLine("══ Settings ═══════════════════════════════");
        var choices = new[] { "Change Coralogix region / endpoint", "Change Coralogix API key", "Back" };
        var choice = Prompt.Select("Choice", choices);
        Console.WriteLine();

        return choice switch
        {
            "Change Coralogix region / endpoint" => ChangeRegion(current),
            "Change Coralogix API key" => ChangeApiKey(current),
            _ => current
        };
    }

    private static SessionConfig ChangeRegion(SessionConfig current)
    {
        var regionInput = Prompt.Input<string>($"Coralogix region ({KnownRegions})", defaultValue: "eu2");
        try
        {
            var newEndpoint = RegionMapper.Resolve(regionInput ?? string.Empty);
            Console.WriteLine($"Endpoint updated to: {newEndpoint}");
            return current with { CxEndpoint = newEndpoint };
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"Unknown region '{regionInput}'. Keeping current endpoint.");
            return current;
        }
    }

    private static SessionConfig ChangeApiKey(SessionConfig current)
    {
        var newKey = Prompt.Password("New Coralogix API key");
        if (!string.IsNullOrWhiteSpace(newKey))
        {
            Console.WriteLine("API key updated.");
            return current with { CxApiKey = newKey };
        }
        Console.Error.WriteLine("Empty key — keeping current.");
        return current;
    }
}
