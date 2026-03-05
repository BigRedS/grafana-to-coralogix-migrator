using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;

namespace GrafanaToCx.Cli.Cli;

/// <summary>
/// Bootstrap and command dispatch. Parses args and routes to CommandHandlers or interactive mode.
/// </summary>
public static class AppRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = ArgumentParser.Parse(args);
        using var bootstrapLoggerFactory = CreateBootstrapLoggerFactory();
        using var runtimeLoggerFactory = TryCreateRuntimeLoggerFactory(parsed);
        var loggerFactory = runtimeLoggerFactory ?? bootstrapLoggerFactory;
        var handlers = new CommandHandlers(loggerFactory);

        try
        {
            return parsed.Command switch
            {
                CommandKind.Interactive => await handlers.RunInteractiveConsoleAsync("migration-settings.json"),
                CommandKind.Convert => await RunConvertFromArgs(handlers, parsed),
                CommandKind.Migrate => await RunMigrateFromArgs(handlers, parsed),
                CommandKind.Verify => await RunVerifyFromArgs(handlers, parsed),
                CommandKind.Import => await RunImportFromArgs(handlers, parsed),
                _ => await handlers.RunInteractiveConsoleAsync("migration-settings.json")
            };
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static ILoggerFactory CreateBootstrapLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
            builder.ClearProviders()
                .AddConsole()
                .SetMinimumLevel(LogLevel.Warning));
    }

    private static ILoggerFactory? TryCreateRuntimeLoggerFactory(ParsedArgs parsed)
    {
        var settingsPath = ResolveSettingsPath(parsed);

        try
        {
            if (!File.Exists(settingsPath))
            {
                WarnLoggingFallback($"settings file '{settingsPath}' was not found");
                return null;
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
                .Build();

            var serilogSection = configuration.GetSection("Serilog");
            if (!serilogSection.Exists())
            {
                WarnLoggingFallback("Serilog section is missing");
                return null;
            }

            var runtimeLogger = CreateRuntimeLoggerFromSection(serilogSection);

            return LoggerFactory.Create(builder =>
                builder.ClearProviders()
                    .AddSerilog(runtimeLogger, dispose: true));
        }
        catch (Exception ex)
        {
            WarnLoggingFallback(ex.Message);
            return null;
        }
    }

    private static string ResolveSettingsPath(ParsedArgs parsed)
    {
        if (parsed.Command == CommandKind.Migrate && !string.IsNullOrWhiteSpace(parsed.Get("settings")))
        {
            return parsed.Get("settings")!;
        }

        return "migration-settings.json";
    }

    private static Serilog.ILogger CreateRuntimeLoggerFromSection(IConfigurationSection serilogSection)
    {
        var minimumLevel = ParseMinimumLevel(serilogSection["MinimumLevel"]);
        var serviceName = serilogSection.GetSection("Properties")["Service"] ?? "grafana-to-cx-cli";

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName);

        var hasSink = false;

        if (serilogSection.GetValue<bool?>("WriteTo:File:Enabled") ?? true)
        {
            hasSink = TryAddFileSink(loggerConfiguration, serilogSection.GetSection("WriteTo").GetSection("File")) || hasSink;
        }

        if (serilogSection.GetValue<bool?>("WriteTo:Otlp:Enabled") ?? true)
        {
            hasSink = TryAddOtlpSink(loggerConfiguration, serilogSection.GetSection("WriteTo").GetSection("Otlp"), serviceName) || hasSink;
        }

        if (!hasSink)
        {
            throw new InvalidOperationException("No Serilog sinks are enabled after applying configuration.");
        }

        return loggerConfiguration.CreateLogger();
    }

    private static bool TryAddFileSink(LoggerConfiguration loggerConfiguration, IConfigurationSection fileSection)
    {
        try
        {
            var path = fileSection["Path"] ?? "logs/grafana-to-cx-.json";
            var rollingInterval = ParseRollingInterval(fileSection["RollingInterval"]);
            var formatter = string.Equals(fileSection["Formatter"], "Json", StringComparison.OrdinalIgnoreCase)
                ? new JsonFormatter()
                : new JsonFormatter();

            loggerConfiguration.WriteTo.File(
                formatter,
                path,
                rollingInterval: rollingInterval);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Serilog file sink initialization failed ({ex.Message}). Continuing without file sink.");
            return false;
        }
    }

    private static bool TryAddOtlpSink(LoggerConfiguration loggerConfiguration, IConfigurationSection otlpSection, string serviceName)
    {
        try
        {
            var endpoint = otlpSection["Endpoint"] ?? "http://localhost:4317";
            var protocol = ParseOtlpProtocol(otlpSection["Protocol"]);

            loggerConfiguration.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = endpoint;
                options.Protocol = protocol;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName
                };
            });

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Serilog OTLP sink initialization failed ({ex.Message}). Continuing without OTLP sink.");
            return false;
        }
    }

    private static LogEventLevel ParseMinimumLevel(string? value)
    {
        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
    }

    private static RollingInterval ParseRollingInterval(string? value)
    {
        return Enum.TryParse<RollingInterval>(value, ignoreCase: true, out var interval)
            ? interval
            : RollingInterval.Day;
    }

    private static OtlpProtocol ParseOtlpProtocol(string? value)
    {
        return Enum.TryParse<OtlpProtocol>(value, ignoreCase: true, out var protocol)
            ? protocol
            : OtlpProtocol.Grpc;
    }

    private static void WarnLoggingFallback(string reason)
    {
        Console.Error.WriteLine($"Warning: Serilog initialization failed ({reason}). Falling back to bootstrap console logging.");
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

    private static async Task<int> RunImportFromArgs(CommandHandlers handlers, ParsedArgs parsed)
    {
        var input = parsed.Get("input");
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("Error: import requires an input directory.");
            return 1;
        }

        var cxApiKey = Environment.GetEnvironmentVariable("CX_API_KEY");
        if (string.IsNullOrWhiteSpace(cxApiKey))
        {
            Console.Error.WriteLine("Error: CX_API_KEY environment variable is required for import.");
            return 1;
        }

        var endpoint = parsed.Get("endpoint") ?? "https://api.coralogix.com/mgmt/openapi/latest";
        return await handlers.RunImportAsync(input, endpoint, cxApiKey, interactive: false);
    }
}
