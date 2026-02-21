using System.Security.Cryptography;
using System.Text;

namespace Linky.Middlewares
{
    /// <summary>
    ///  IMPORTANT: ETag Middleware for FusionCache Cached Responses 
    /// 
    /// WHY THIS MIDDLEWARE IS NEEDED:
    /// ------------------------------
    /// Delta.EF automatically generates ETags and handles 304 Not Modified responses,
    /// BUT ONLY for data returned DIRECTLY from EF Core queries!
    /// 
    /// PROBLEM:
    /// When using FusionCache (cacheService.GetAsync), data comes from:
    ///   - L1 Memory Cache (RAM), OR
    ///   - L2 Distributed Cache (Redis/Valkey)
    /// 
    /// Delta.EF CANNOT track these responses because they bypass EF Core entirely!
    /// Result: No ETag generated - No 304 Not Modified - Always full response sent
    /// 
    /// SOLUTION:
    /// This middleware works COMPLEMENTARY with Delta.EF:
    ///   1. Delta.EF handles ETags for direct EF Core queries
    ///   2. CacheETagMiddleware handles ETags for FusionCache responses
    ///   3. Both enable 304 Not Modified for bandwidth savings
    /// 
    /// HOW IT WORKS:
    /// - Intercepts ALL GET responses
    /// - Checks if Delta.EF already set an ETag (direct DB query)
    /// - If NO ETag exists (cached response), generates one from response content
    /// - Verifies If-None-Match header and returns 304 if ETag matches
    /// 
    /// BENEFITS:
    ///  Reduced bandwidth (304 responses are ~50 bytes vs KB of JSON)
    ///  Faster responses (304 returned instantly without processing)
    ///  Better mobile experience (saves cellular data)
    ///  Works with both Delta.EF (DB) and FusionCache (cache)
    /// </summary>
    public class CacheETagMiddleware
    {
        private readonly RequestDelegate _next;

        public CacheETagMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only process GET requests
            if (context.Request.Method != HttpMethods.Get)
            {
                await _next(context);
                return;
            }

            var originalBodyStream = context.Response.Body;

            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            try
            {
                await _next(context);

                // Only process successful responses
                if (context.Response.StatusCode == 200)
                {
                    responseBody.Seek(0, SeekOrigin.Begin);
                    var responseBytes = responseBody.ToArray();

                    // CRITICAL: Check if Delta.EF already generated an ETag
                    // Delta.EF generates ETags ONLY for direct EF Core DB queries
                    // FusionCache responses (from RAM/Redis) bypass EF Core - no ETag from Delta
                    // If no ETag exists, we generate one from the cached response content
                    string etag;
                    if (!context.Response.Headers.ContainsKey("ETag"))
                    {
                        // No ETag from Delta.EF - This is a cached response
                        // Generate ETag from response body for 304 support
                        etag = GenerateETag(responseBytes);
                        context.Response.Headers.ETag = etag;
                    }
                    else
                    {
                        // ETag already exists from Delta.EF - This is a direct DB query
                        // Reuse Delta's ETag (it's already optimized for PostgreSQL change tracking)
                        etag = context.Response.Headers.ETag.ToString();
                    }

                    // Check If-None-Match header from client (browser/app sent previous ETag)
                    if (context.Request.Headers.TryGetValue("If-None-Match", out var incomingETag))
                    {
                        if (incomingETag.ToString() == etag)
                        {
                            // ETags match - Content hasn't changed
                            // Return 304 Not Modified (saves bandwidth, ultra-fast response)
                            context.Response.StatusCode = StatusCodes.Status304NotModified;
                            context.Response.Body = originalBodyStream;
                            context.Response.ContentLength = 0;
                            return;
                        }
                    }

                    // ETags don't match or no If-None-Match header - return full response with ETag
                    context.Response.ContentLength = responseBytes.Length;
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream);
                }
                else
                {
                    // Non-200 responses - just pass through
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream);
                }
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        /// <summary>
        /// Generates a stable, deterministic ETag using SHA256 hash of the response content.
        /// 
        /// Format: W/"base64hash" (Weak ETag)
        /// 
        /// WHY WEAK ETAG (W/):
        /// - Allows compatibility with gzip/compression (content may be compressed differently)
        /// - Still provides cache validation (304 Not Modified)
        /// - Better performance than strong ETags
        /// - Recommended for cached API responses
        /// 
        /// PERFORMANCE:
        /// SHA256.HashData is highly optimized in .NET and uses hardware acceleration when available.
        /// The hash is deterministic - same content ALWAYS produces same ETag.
        /// </summary>
        private static string GenerateETag(byte[] content)
        {
            var hash = SHA256.HashData(content);
            var base64Hash = Convert.ToBase64String(hash);
            // Use weak ETag (W/) for better performance and compatibility with gzip/compression
            return $"W/\"{base64Hash}\"";
        }
    }
}
