using GrafanaToCx.Core.Migration;
using Sharprompt;

namespace GrafanaToCx.Cli.Cli;

/// <summary>
/// Sharprompt-based input helpers for session config, region, and password.
/// </summary>
public static class PromptInput
{
    private const string KnownRegions = "eu1, eu2, us1, us2, ap1, ap2, ap3, in1";

    public static SessionConfig? PromptSessionConfig()
    {
        var regionInput = Prompt.Input<string>($"Coralogix region ({KnownRegions})", defaultValue: "eu2");

        string cxEndpoint;
        try
        {
            cxEndpoint = RegionMapper.Resolve(regionInput ?? string.Empty);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"Unknown region '{regionInput}'. Valid: {KnownRegions}");
            return null;
        }

        var apiKey = Environment.GetEnvironmentVariable("CX_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = Prompt.Password("Coralogix API key", validators: [Validators.Required()]);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("API key cannot be empty.");
                return null;
            }
        }
        else
        {
            Console.WriteLine("Using CX_API_KEY from environment.");
        }

        Console.WriteLine($"Connected to: {cxEndpoint}");
        return new SessionConfig(cxEndpoint, apiKey);
    }

    public static string PromptPassword(string message)
    {
        return Prompt.Password(message, validators: [Validators.Required()]);
    }

    public static string? PromptPasswordOptional(string message)
    {
        return Prompt.Password(message);
    }

    public static string PromptRegion(string message, string defaultValue = "eu2")
    {
        return Prompt.Input<string>(message, defaultValue);
    }

    public static bool PromptConfirm(string message, bool defaultValue = false)
    {
        return Prompt.Confirm(message, defaultValue);
    }

    public static string AskInput(string message, string? defaultValue = null)
    {
        return defaultValue is null
            ? Prompt.Input<string>(message, validators: [Validators.Required()])
            : Prompt.Input<string>(message, defaultValue);
    }

    public static string? AskInputOptional(string message, string? defaultValue = null)
    {
        return Prompt.Input<string>(message, defaultValue ?? string.Empty);
    }

    public static T PromptSelect<T>(string message, IEnumerable<T> items, Func<T, string>? displaySelector = null) where T : notnull
    {
        return displaySelector is null
            ? Prompt.Select(message, items)
            : Prompt.Select(message, items, textSelector: displaySelector);
    }

    public static IReadOnlyList<T> PromptMultiSelect<T>(string message, IEnumerable<T> items, Func<T, string>? displaySelector = null) where T : notnull
    {
        var result = displaySelector is null
            ? Prompt.MultiSelect(message, items)
            : Prompt.MultiSelect(message, items, textSelector: displaySelector);
        return result.ToList();
    }
}
