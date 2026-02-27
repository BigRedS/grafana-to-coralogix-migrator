using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GrafanaToCx.Core.Migration;

public enum CheckpointStatus
{
    Pending,
    Completed,
    FailedCritical,
    FailedRetryable
}

public sealed class CheckpointEntry
{
    public string GrafanaUid { get; set; } = string.Empty;
    public string GrafanaTitle { get; set; } = string.Empty;
    public string FolderTitle { get; set; } = string.Empty;

    [JsonConverter(typeof(StringEnumConverter))]
    public CheckpointStatus Status { get; set; } = CheckpointStatus.Pending;

    public string? CxDashboardId { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
}
