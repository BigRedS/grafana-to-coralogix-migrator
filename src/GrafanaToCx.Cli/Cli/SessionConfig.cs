namespace GrafanaToCx.Cli.Cli;

public sealed record SessionConfig(string CxEndpoint, string CxApiKey, string? GrafanaApiKey = null);
