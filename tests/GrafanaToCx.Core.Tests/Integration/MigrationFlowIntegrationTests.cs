using GrafanaToCx.Cli.Cli;
using GrafanaToCx.Core.Migration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests.Integration;

public sealed class MigrationFlowIntegrationTests
{
    private const string IntegrationSettingsEnvVar = "GRAFANA_TO_CX_INTEGRATION_SETTINGS";

    [IntegrationSettingsFact]
    [Trait("Category", "Integration")]
    public async Task RunMigrateAsync_ExecutesLiveFlow_AndProducesCheckpointAndReport()
    {
        var sourceSettingsPath = Environment.GetEnvironmentVariable(IntegrationSettingsEnvVar)!;
        var tempDir = Path.Combine(Path.GetTempPath(), $"migration-flow-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimeSettingsPath = Path.Combine(tempDir, "migration-settings.runtime.json");
            var checkpointPath = Path.Combine(tempDir, "migration-checkpoint.integration.json");
            var reportPath = Path.Combine(tempDir, "migration-report.integration.txt");
            var backupPath = Path.Combine(tempDir, "grafana-backup.integration.zip");

            await MaterializeRuntimeSettingsAsync(sourceSettingsPath, runtimeSettingsPath, checkpointPath, reportPath, backupPath);

            using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var handlers = new CommandHandlers(loggerFactory);

            var exitCode = await handlers.RunMigrateAsync(runtimeSettingsPath, interactive: false);
            Assert.Equal(0, exitCode);

            Assert.True(File.Exists(checkpointPath), $"Expected checkpoint file at '{checkpointPath}'.");

            var checkpointJson = await File.ReadAllTextAsync(checkpointPath);
            var entries = JsonConvert.DeserializeObject<Dictionary<string, CheckpointEntry>>(checkpointJson)
                ?? new Dictionary<string, CheckpointEntry>();

            Assert.NotEmpty(entries);
            Assert.Contains(
                entries.Values,
                entry => entry.Status == CheckpointStatus.Completed && !string.IsNullOrWhiteSpace(entry.CxDashboardId));

            Assert.True(File.Exists(reportPath), $"Expected migration report file at '{reportPath}'.");
            var reportText = await File.ReadAllTextAsync(reportPath);
            Assert.Contains("Migration Report", reportText);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task MaterializeRuntimeSettingsAsync(
        string sourceSettingsPath,
        string runtimeSettingsPath,
        string checkpointPath,
        string reportPath,
        string backupPath)
    {
        var sourceJson = await File.ReadAllTextAsync(sourceSettingsPath);
        var settings = JObject.Parse(sourceJson);

        if (settings["migration"] is not JObject migrationSettings)
        {
            migrationSettings = new JObject();
            settings["migration"] = migrationSettings;
        }

        migrationSettings["checkpointFile"] = checkpointPath;
        migrationSettings["reportFile"] = reportPath;
        migrationSettings["backupFile"] = backupPath;

        await File.WriteAllTextAsync(runtimeSettingsPath, settings.ToString(Formatting.Indented));
    }

    private sealed class IntegrationSettingsFactAttribute : FactAttribute
    {
        public IntegrationSettingsFactAttribute()
        {
            var settingsPath = Environment.GetEnvironmentVariable(IntegrationSettingsEnvVar);
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                Skip = $"Skipping live integration test: set {IntegrationSettingsEnvVar} to a migration settings JSON path.";
                return;
            }

            if (!File.Exists(settingsPath))
            {
                Skip = $"Skipping live integration test: file from {IntegrationSettingsEnvVar} does not exist: '{settingsPath}'.";
            }
        }
    }
}
