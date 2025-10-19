using Polly;
using Polly.RateLimit;

namespace Linky.Config
{
    public static class PollyConfig
    {
        public static IAsyncPolicy<HttpResponseMessage> HttpRetryPolicy()
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r =>
                    (int)r.StatusCode >= 500 ||
                    r.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                    r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .Or<HttpRequestException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100), // 200ms, 400ms, 800ms
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        var error = outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString();
                        Console.WriteLine($"[HTTP Retry] Attempt {retryCount} after {timespan.TotalMilliseconds}ms - {error}");
                    });
        }

        public static IAsyncPolicy<HttpResponseMessage> HttpCircuitBreakerPolicy()
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .Or<HttpRequestException>()
                .Or<TaskCanceledException>()
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: 0.5,              // Open if 50% of requests fail
                    samplingDuration: TimeSpan.FromSeconds(10),
                    minimumThroughput: 8,               // Need at least 8 requests in sample
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, duration) =>
                    {
                        Console.WriteLine($"[Circuit OPEN] Breaking for {duration.TotalSeconds}s");
                    },
                    onReset: () => Console.WriteLine("[Circuit CLOSED] Service recovered"),
                    onHalfOpen: () => Console.WriteLine("[Circuit HALF-OPEN] Testing service"));
        }

        public static IAsyncPolicy<HttpResponseMessage> HttpTimeoutPolicy()
        {
            return Policy
                .TimeoutAsync<HttpResponseMessage>(
                    timeout: TimeSpan.FromSeconds(30), // Adjust based on your upstream SLA
                    timeoutStrategy: Polly.Timeout.TimeoutStrategy.Pessimistic,
                    onTimeoutAsync: (context, timespan, task) =>
                    {
                        Console.WriteLine($"[Timeout] Request exceeded {timespan.TotalSeconds}s");
                        return Task.CompletedTask;
                    });
        }

        // ALL HTTP POLICIES - Critical for Reverse Proxy
        public static IAsyncPolicy<HttpResponseMessage> HttpResiliencePolicy()
        {
            return Policy.WrapAsync(
                HttpCircuitBreakerPolicy(),
                HttpRetryPolicy(),
                HttpTimeoutPolicy()
            );
        }

        public static AsyncRateLimitPolicy UpstreamRateLimitPolicy(int requestsPerSecond = 100)
        {
            return Policy
                .RateLimitAsync(
                    numberOfExecutions: requestsPerSecond,
                    perTimeSpan: TimeSpan.FromSeconds(1),
                    maxBurst: requestsPerSecond / 2); // Allow 50% burst
        }


        // ============================================
        // BULKHEAD - Resource Isolation
        // ============================================

        /// <summary>
        /// Bulkhead prevents one failing operation from consuming all threads
        /// CRITICAL for maintaining responsiveness under load
        /// </summary>

        public static IAsyncPolicy BulkheadPolicy(int maxParallel = 100, int maxQueue = 200)
        {
            return Policy
                .BulkheadAsync(
                    maxParallelization: maxParallel,
                    maxQueuingActions: maxQueue,
                    onBulkheadRejectedAsync: context =>
                    {
                        Console.WriteLine($"[Bulkhead] Request rejected - system at capacity");
                        return Task.CompletedTask;
                    });
        }
    }
}
