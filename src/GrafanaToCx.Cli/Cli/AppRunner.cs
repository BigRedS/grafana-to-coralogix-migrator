using Microsoft.Extensions.Logging;

namespace GrafanaToCx.Cli.Cli;

/// <summary>
/// Bootstrap and command dispatch. Parses args and routes to CommandHandlers or interactive mode.
/// </summary>
public static class AppRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var handlers = new CommandHandlers(loggerFactory);
        var parsed = ArgumentParser.Parse(args);

        return parsed.Command switch
        {
            CommandKind.Interactive => await handlers.RunInteractiveConsoleAsync("migration-settings.json"),
            CommandKind.Convert => await RunConvertFromArgs(handlers, parsed),
            CommandKind.Migrate => await RunMigrateFromArgs(handlers, parsed),
            CommandKind.Verify => await RunVerifyFromArgs(handlers, parsed),
            _ => await handlers.RunInteractiveConsoleAsync("migration-settings.json")
        };
    }

    private static async Task<int> RunConvertFromArgs(CommandHandlers handlers, ParsedArgs parsed)
    {
        var input = parsed.Get("input");
        if (string.IsNullOrEmpty(input))
        {
            Console.Error.WriteLine("Error: convert requires an input file or directory.");
            return 1;
        }
        return await handlers.RunConvertAsync(input, parsed.Get("output"));
    }

    private static async Task<int> RunMigrateFromArgs(CommandHandlers handlers, ParsedArgs parsed)
    {
        var settings = parsed.Get("settings") ?? "migration-settings.json";
        var interactive = parsed.GetBool("interactive");
        return await handlers.RunMigrateAsync(settings, interactive);
    }

    private static async Task<int> RunVerifyFromArgs(CommandHandlers handlers, ParsedArgs parsed)
    {
        var input = parsed.Get("input");
        if (string.IsNullOrEmpty(input))
        {
            Console.Error.WriteLine("Error: verify requires an input file.");
            return 1;
        }
        var endpoint = parsed.Get("endpoint") ?? "https://api.coralogix.com/mgmt/openapi/latest";
        var dashboardId = parsed.Get("dashboard-id");
        return await handlers.RunVerifyAsync(input, endpoint, dashboardId);
    }
}
