using System.Net;

namespace GrafanaToCx.Core.Migration;

public enum FailureKind
{
    Critical,
    Retryable
}

public static class RetryPolicy
{
    private static readonly HashSet<HttpStatusCode> CriticalStatusCodes =
    [
        HttpStatusCode.BadRequest,          // 400
        HttpStatusCode.Unauthorized,        // 401
        HttpStatusCode.Forbidden,           // 403
        HttpStatusCode.NotFound,            // 404
        HttpStatusCode.UnprocessableEntity, // 422
    ];

    public static FailureKind Classify(HttpStatusCode statusCode) =>
        CriticalStatusCodes.Contains(statusCode) ? FailureKind.Critical : FailureKind.Retryable;

    public static TimeSpan ComputeDelay(int retryCount, int initialDelaySeconds)
    {
        var baseDelay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, retryCount));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        var maxDelay = TimeSpan.FromMinutes(10);
        return baseDelay + jitter > maxDelay ? maxDelay : baseDelay + jitter;
    }
}
