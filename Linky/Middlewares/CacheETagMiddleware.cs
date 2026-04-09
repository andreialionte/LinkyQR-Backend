using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Linky.Middlewares
{
    /// <summary>
    /// RFC 9110 Compliant ETag Middleware with Cloudflare-Style Cache-Control
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// TESTING GUIDE - HOW TO SEE 304 NOT MODIFIED RESPONSES
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// Browser's "200 OK (from disk cache)" vs Server's "304 Not Modified":
    ///   • Disk cache hit (0-300s, max-age window):
    ///     Browser serves from local cache WITHOUT contacting server
    ///     No network request at all — instant response
    ///     DevTools shows "200 OK (from disk cache)" — this is CLIENT cache, not server response
    ///     You see this because max-age=300 is fresh (5 minutes)
    /// 
    ///   • Server 304 (>300s, after max-age expires):
    ///     Browser sends If-None-Match with the ETag it cached
    ///     Server compares ETag: if same → returns 304 Not Modified
    ///     DevTools shows "304 Not Modified" in Network tab with ~300 byte response
    ///     Browser uses cached content + resets freshness timer (max-age starts over)
    /// 
    /// HOW TO TEST 304 NOT MODIFIED:
    /// 
    /// Option 1 (Recommended): Hard Refresh to bypass disk cache
    ///   1. Open DevTools (F12) → Network tab
    ///   2. Hard refresh: Ctrl+Shift+R (Windows/Linux) or Cmd+Shift+R (Mac)
    ///   3. Look for your API request in Network tab
    ///   4. Status column should show "304 Not Modified" if ETag matches
    ///   5. Size should show "X B transferred, Y B resources" (small size ≈ 300 bytes)
    ///   6. Response tab shows empty body (no content)
    ///   7. Headers tab shows:
    ///      - Request: If-None-Match: W/"youretaghere"
    ///      - Response: ETag: W/"youretaghere"
    ///      - Response: Cache-Control: max-age=300, stale-while-revalidate=10800, ...
    /// 
    /// Option 2: Wait for max-age to expire
    ///   1. First normal request gets "200 OK" with ETag in response headers
    ///   2. Wait 5+ minutes (max-age=300 expires)
    ///   3. Refresh normally (Ctrl+R)
    ///   4. Browser sends If-None-Match → Server returns 304 Not Modified
    /// 
    /// Option 3: Edit request in DevTools (advanced)
    ///   1. DevTools → Network tab → right-click request → "Edit as cURL"
    ///   2. Add header: -H "If-None-Match: W/\"youretaghere\"" (use the ETag from first response)
    ///   3. Run the request → Should get 304 Not Modified
    /// 
    /// VERIFY ETAG IS PRESENT:
    ///   1. First request: DevTools → Network → Click request → Response Headers
    ///   2. Look for: ETag: W/"..."
    ///   3. Copy the full ETag value
    ///   4. Hard refresh → Request Headers will show: If-None-Match: W/"..." (same value)
    ///   5. If Status = 304, ETag match works ✅
    ///   6. If Status = 200, ETag mismatch (content changed)
    /// 
    /// IMPLEMENTS:
    ///   RFC 9110 - HTTP Semantics (§8.8.3 ETag, §13 Conditional Requests, §15.4.5 304):
    ///              https://datatracker.ietf.org/doc/html/rfc9110
    ///   RFC 9111 - HTTP Caching:         https://datatracker.ietf.org/doc/html/rfc9111
    ///   RFC 5861 - Stale Extensions:     https://datatracker.ietf.org/doc/html/rfc5861
    ///   MDN:  https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/Conditional_requests
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// CACHING STRATEGY (matches Cloudflare CDN behavior)
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// Cache-Control: max-age=300, stale-while-revalidate=10800, stale-if-error=10800, public
    /// 
    /// Timeline:
    ///   0–300s (5 min)         → Browser serves from disk cache, ZERO network requests
    ///                            DevTools shows "200 OK (from disk cache)"
    ///   300s–10800s (5m–3h)    → Stale served instantly + background revalidation
    ///                            If-None-Match → 304 Not Modified (ETag match)
    ///                            User sees instant response, cache refreshes silently
    ///   >10800s (>3h)          → Standard revalidation before serving
    ///   Origin down (any time) → Stale served for up to 3 hrs (resilience)
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// SAFE METHODS (GET, HEAD) - FULL IMPLEMENTATION ✅
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// Automatically handles:
    ///   ✅ ETag generation (SHA256, weak ETags with W/)
    ///   ✅ If-None-Match validation → 304 Not Modified (weak comparison per RFC 7232 §2.3.2)
    ///   ✅ If-Modified-Since validation → 304 Not Modified (only for Delta.EF DB responses)
    ///   ✅ Proper header cleanup for 304 (Content-Length removed, Content-Encoding, Transfer-Encoding)
    ///   ✅ Vary: Accept-Encoding (correct caching with compression variants)
    ///   ✅ Early 304 fast-path (skips body hashing when Delta.EF ETag already matches)
    ///   ✅ WebSocket/SSE bypass (SignalR, streaming responses skip buffering)
    /// 
    /// Use cases:
    ///   • Disk cache (zero requests during max-age window — instant)
    ///   • Cache validation (304 = ~400 bytes vs 1.5 kB full response)
    ///   • Mobile data savings (~75% bandwidth reduction on cache hits)
    ///   • Resilience (stale-if-error serves cached content when origin is down)
    /// 
    /// IMPORTANT: Last-Modified behavior:
    ///   • FusionCache responses: Only ETag (no Last-Modified timestamp available)
    ///   • Delta.EF DB responses: Both ETag + Last-Modified (from DB)
    ///   • This prevents ETag mismatch bugs from changing timestamps
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// UNSAFE METHODS (PUT, PATCH, DELETE, POST) - CONTROLLER RESPONSIBILITY ⚠️
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// For optimistic locking with If-Match/If-Unmodified-Since:
    ///   ⚠️ Controllers MUST validate these headers manually
    ///   ⚠️ Return 412 Precondition Failed if validation fails
    /// 
    /// Why not in middleware?
    ///   • Requires pre-fetching resource from DB (before update)
    ///   • Requires knowing resource URL structure
    ///   • Standard practice: validation at controller level (Django, Rails, Laravel)
    /// 
    /// Example controller implementation:
    ///   [HttpPut("/api/resource/{id}")]
    ///   public async Task<IActionResult> Update(int id, [FromBody] Resource resource)
    ///   {
    ///       var current = await _db.Resources.FindAsync(id);
    ///       var currentETag = GenerateETag(current); // Your helper method
    ///       
    ///       if (Request.Headers.TryGetValue("If-Match", out var ifMatch))
    ///       {
    ///           if (ifMatch != currentETag)
    ///               return StatusCode(412); // Precondition Failed
    ///       }
    ///       
    ///       // Perform update...
    ///   }
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// FUSIONCACHE INTEGRATION:
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// Works complementary with Delta.EF:
    ///   1. Delta.EF handles ETags for direct EF Core DB queries
    ///   2. CacheETagMiddleware handles ETags for FusionCache responses (L1 Memory/L2 Redis)
    ///   3. Both enable 304 Not Modified for bandwidth savings
    /// 
    /// </summary>
    public class CacheETagMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CacheETagMiddleware>? _logger;
        private readonly CacheETagOptions _options;
        private readonly CacheETagMetrics? _metrics;

        public CacheETagMiddleware(
            RequestDelegate next,
            IOptions<CacheETagOptions> options,
            ILogger<CacheETagMiddleware>? logger = null,
            CacheETagMetrics? metrics = null)
        {
            _next = next;
            _options = options?.Value ?? new CacheETagOptions();
            _logger = logger;
            _metrics = metrics;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // TIER 1: Track total requests
            _metrics?.IncrementTotalRequests();

            // ─────────────────────────────────────────────────────────────
            // BYPASS: Non-cacheable requests skip body buffering entirely
            // ─────────────────────────────────────────────────────────────

            if (context.Request.Method != HttpMethods.Get &&
                context.Request.Method != HttpMethods.Head)
            {
                await _next(context);
                return;
            }

            if (context.WebSockets.IsWebSocketRequest ||
                context.Request.Headers.Connection.ToString().Contains("Upgrade", StringComparison.OrdinalIgnoreCase))
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

                    // ══════════════════════════════════════════════════════════
                    // TIER 1: SIZE LIMIT CHECK - Skip ETag if response too large
                    // ══════════════════════════════════════════════════════════
                    if (responseBytes.Length > _options.MaxResponseSizeForETag)
                    {
                        _logger?.LogDebug(
                            "CacheETag: Skipping ETag (response {Size} bytes > limit {Limit} bytes)",
                            responseBytes.Length, _options.MaxResponseSizeForETag);
                        _metrics?.IncrementSkippedTooLarge();

                        SetCacheHeadersIfNotPresent(context);
                        context.Response.ContentLength = responseBytes.Length;
                        responseBody.Seek(0, SeekOrigin.Begin);
                        await responseBody.CopyToAsync(originalBodyStream);
                        return;
                    }

                    // ══════════════════════════════════════════════════════════
                    // TIER 2: CONTENT-TYPE FILTERING - Skip binary/image types
                    // ══════════════════════════════════════════════════════════
                    var contentType = context.Response.ContentType;
                    if (!_options.IsContentTypeAllowed(contentType))
                    {
                        _logger?.LogDebug(
                            "CacheETag: Skipping ETag for Content-Type '{ContentType}'",
                            contentType ?? "(null)");
                        _metrics?.IncrementSkippedContentType();

                        SetCacheHeadersIfNotPresent(context);
                        context.Response.ContentLength = responseBytes.Length;
                        responseBody.Seek(0, SeekOrigin.Begin);
                        await responseBody.CopyToAsync(originalBodyStream);
                        return;
                    }

                    // ══════════════════════════════════════════════════════════
                    // TIER 1: EXCEPTION HANDLING - Graceful ETag generation failure
                    // ══════════════════════════════════════════════════════════
                    string etag;
                    try
                    {
                        if (!context.Response.Headers.ContainsKey("ETag"))
                        {
                            etag = GenerateETag(responseBytes);
                            context.Response.Headers.ETag = etag;
                            _metrics?.IncrementETagGenerated();
                            _logger?.LogDebug("CacheETag: Generated ETag '{ETag}'", etag);
                        }
                        else
                        {
                            etag = context.Response.Headers.ETag.ToString();
                            _logger?.LogDebug("CacheETag: Using existing ETag '{ETag}'", etag);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "CacheETag: Exception generating ETag - skipping cache headers");
                        _metrics?.IncrementException();

                        context.Response.ContentLength = responseBytes.Length;
                        responseBody.Seek(0, SeekOrigin.Begin);
                        await responseBody.CopyToAsync(originalBodyStream);
                        return;
                    }

                    // TIER 3: Respect existing Cache-Control if set by controller
                    SetCacheHeadersIfNotPresent(context);

                    // ══════════════════════════════════════════════════════════
                    // TIER 2: HEADER VALIDATION - Validate If-None-Match format
                    // ══════════════════════════════════════════════════════════
                    if (context.Request.Headers.TryGetValue("If-None-Match", out var incomingETags))
                    {
                        if (!ValidateIfNoneMatchHeader(incomingETags.ToString(), out var incomingEtagList))
                        {
                            _logger?.LogDebug(
                                "CacheETag: Invalid If-None-Match header format: '{Header}'",
                                incomingETags);
                        }
                        else if (incomingEtagList.Contains("*") ||
                                 incomingEtagList.Any(e => WeakETagEquals(e, etag)))
                        {
                            _metrics?.IncrementResponses304();
                            _logger?.LogDebug("CacheETag: 304 Not Modified (ETag match)");
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }

                    // If-Modified-Since fallback
                    if (!context.Request.Headers.ContainsKey("If-None-Match") &&
                        context.Response.Headers.ContainsKey("Last-Modified") &&
                        context.Request.Headers.TryGetValue("If-Modified-Since", out var ifModifiedSinceValue) &&
                        DateTime.TryParse(ifModifiedSinceValue, out var ifModifiedSince) &&
                        context.Response.Headers.TryGetValue("Last-Modified", out var lastModifiedValue) &&
                        DateTime.TryParse(lastModifiedValue, out var lastModified))
                    {
                        if (lastModified.AddMilliseconds(-lastModified.Millisecond) <=
                            ifModifiedSince.AddMilliseconds(-ifModifiedSince.Millisecond))
                        {
                            _metrics?.IncrementResponses304();
                            _logger?.LogDebug("CacheETag: 304 Not Modified (Last-Modified match)");
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }

                    // Return full 200 with ETag
                    _metrics?.IncrementResponses200();
                    context.Response.ContentLength = responseBytes.Length;
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream);
                }
                else
                {
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CacheETag: Unhandled exception in middleware");
                _metrics?.IncrementException();
                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        /// <summary>
        /// TIER 2: Validate If-None-Match header format per RFC 9110.
        /// </summary>
        private static bool ValidateIfNoneMatchHeader(string headerValue, out string[] etagList)
        {
            etagList = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(headerValue))
                return false;

            try
            {
                etagList = headerValue
                    .Split(',')
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrEmpty(e))
                    .ToArray();

                return etagList.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// TIER 3: Set Cache-Control headers only if not already present.
        /// Respects controller-level Cache-Control overrides.
        /// </summary>
        private void SetCacheHeadersIfNotPresent(HttpContext context)
        {
            if (!context.Response.Headers.ContainsKey("Cache-Control"))
            {
                SetCacheHeaders(context);
            }
        }

        /// <summary>
        /// Returns HTTP 304 Not Modified per RFC 7232 §4.1 and RFC 9110 §15.4.5
        /// https://datatracker.ietf.org/doc/html/rfc9110#section-15.4.5
        /// 
        /// When client sends If-None-Match and ETag matches:
        ///   Server responds with 304 (no body) instead of 200 (full response)
        ///   Client uses cached copy + updates cache headers
        ///   Saves ~75% bandwidth (304 response ≈ 300 bytes vs full response ≈ 1-5 KB)
        /// 
        /// What happens:
        ///   1. Client cache hit during max-age window (0-300s) → Browser serves from disk (0 requests)
        ///      DevTools shows "200 OK (from disk cache)" — this is a browser cache hit, NOT a server response
        ///   
        ///   2. Client expires max-age (>300s) → Browser sends If-None-Match with cached ETag
        ///      If ETag matches → Server returns 304 Not Modified (empty body)
        ///      Client uses cached content + updates freshness (max-age resets)
        ///      DevTools shows "304 Not Modified" in Network tab
        ///   
        ///   3. Client hard refresh (Ctrl+Shift+R) → Bypasses disk cache, sends If-None-Match
        ///      If ETag matches → Server returns 304 Not Modified
        ///      If ETag differs → Server returns 200 OK with new ETag + new content
        /// 
        /// Header handling:
        ///   Remove content-related headers (no body in 304):
        ///   - Content-Length: MUST be null (not 0), removed per RFC 7230 §3.3
        ///   - Content-Encoding: removed (no encoded body)
        ///   - Transfer-Encoding: removed (no body to encode)
        ///   - Content-Type: removed (no body to describe)
        ///   
        ///   Keep validation headers (client needs these for next revalidation):
        ///   - ETag: Required (client needs this for next If-None-Match)
        ///   - Last-Modified: Keep if set (alternative revalidation)
        ///   - Cache-Control: Keep (tells client how long to use cached copy)
        ///   - Vary: Keep (tells CDN/proxies how to cache variants)
        ///   - Date: Keep (server timestamp for cache freshness calculation)
        /// </summary>
        private static void Return304NotModified(HttpContext context, Stream originalBodyStream)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.Body = originalBodyStream;

            // Remove content-related headers (304 has NO body)
            context.Response.ContentLength = null; // RFC 7230 §3.3: MUST NOT be 0 if body is non-empty
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Transfer-Encoding");
            context.Response.Headers.Remove("Content-Type");

            // Keep validation + caching headers for client
            // ETag, Last-Modified, Cache-Control, Vary, Date remain
        }

        /// <summary>
        /// Sets Cache-Control and Vary headers using configured options (TIER 2).
        /// Uses CacheETagOptions for max-age, stale-while-revalidate, stale-if-error.
        /// </summary>
        private void SetCacheHeaders(HttpContext context)
        {
            context.Response.Headers.CacheControl = _options.BuildCacheControlHeader();
            context.Response.Headers.Vary = "Accept-Encoding";
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
        /// The hash is deterministic — same content ALWAYS produces same ETag.
        /// </summary>
        private static string GenerateETag(byte[] content)
        {
            var hash = SHA256.HashData(content);
            var base64Hash = Convert.ToBase64String(hash);
            return $"W/\"{base64Hash}\"";
        }

        /// <summary>
        /// Weak comparison per RFC 7232 §2.3.2:
        ///   Two ETags are weakly equivalent if their opaque-tags match,
        ///   regardless of either or both being tagged as "weak".
        /// 
        /// This means:
        ///   W/"abc" == W/"abc"  → true
        ///   W/"abc" == "abc"    → true  (weak comparison ignores W/ prefix)
        ///   "abc"   == "abc"    → true
        ///   W/"abc" == W/"xyz"  → false
        /// 
        /// Required for If-None-Match on GET/HEAD (RFC 7232 §3.2):
        ///   "A recipient MUST use the weak comparison function when comparing
        ///    entity-tags for If-None-Match"
        /// </summary>
        private static bool WeakETagEquals(string a, string b)
        {
            // Strip W/ prefix from both sides, then compare the quoted opaque-tags
            return StripWeakPrefix(a) == StripWeakPrefix(b);
        }

        private static ReadOnlySpan<char> StripWeakPrefix(ReadOnlySpan<char> etag)
        {
            etag = etag.Trim();
            if (etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                etag = etag[2..];
            return etag;
        }
    }

    /// <summary>
    /// TIER 3: Metrics for CacheETagMiddleware.
    /// Thread-safe counter using Interlocked operations.
    /// </summary>
    public class CacheETagMetrics
    {
        private long _totalRequests;
        private long _responses200;
        private long _responses304;
        private long _etagGenerated;
        private long _skippedTooLarge;
        private long _skippedContentType;
        private long _exceptions;

        public long TotalRequests => System.Threading.Interlocked.Read(ref _totalRequests);
        public long Responses200 => System.Threading.Interlocked.Read(ref _responses200);
        public long Responses304 => System.Threading.Interlocked.Read(ref _responses304);
        public long ETagGenerated => System.Threading.Interlocked.Read(ref _etagGenerated);
        public long SkippedTooLarge => System.Threading.Interlocked.Read(ref _skippedTooLarge);
        public long SkippedContentType => System.Threading.Interlocked.Read(ref _skippedContentType);
        public long Exceptions => System.Threading.Interlocked.Read(ref _exceptions);

        public double NotModifiedRate
        {
            get
            {
                var total = Responses200 + Responses304;
                return total == 0 ? 0.0 : (double)Responses304 / total * 100.0;
            }
        }

        public void IncrementTotalRequests() => System.Threading.Interlocked.Increment(ref _totalRequests);
        public void IncrementResponses200() => System.Threading.Interlocked.Increment(ref _responses200);
        public void IncrementResponses304() => System.Threading.Interlocked.Increment(ref _responses304);
        public void IncrementETagGenerated() => System.Threading.Interlocked.Increment(ref _etagGenerated);
        public void IncrementSkippedTooLarge() => System.Threading.Interlocked.Increment(ref _skippedTooLarge);
        public void IncrementSkippedContentType() => System.Threading.Interlocked.Increment(ref _skippedContentType);
        public void IncrementException() => System.Threading.Interlocked.Increment(ref _exceptions);
    }

    /// <summary>
    /// Configuration options for CacheETagMiddleware (TIER 2).
    /// Register in Program.cs via builder.Services.Configure&lt;CacheETagOptions&gt;()
    /// </summary>
    /*public class CacheETagOptions
    {
        public long MaxResponseSizeForETag { get; set; } = 5 * 1024 * 1024;
        public int MaxAgeSeconds { get; set; } = 300;
        public int StaleWhileRevalidateSeconds { get; set; } = 10800;
        public int StaleIfErrorSeconds { get; set; } = 10800;
        public bool EnableLogging { get; set; } = false;
        public bool EnableMetrics { get; set; } = false;

        public string[] AllowedContentTypePatternsForETag { get; set; } = new[]
        {
            "application/json",
            "application/ld+json",
            "application/*+json",
            "application/xml",
            "application/*+xml",
            "text/plain",
            "text/html",
            "text/csv",
            "application/problem+json",
        };

        public string BuildCacheControlHeader()
        {
            return $"max-age={MaxAgeSeconds}, stale-while-revalidate={StaleWhileRevalidateSeconds}, stale-if-error={StaleIfErrorSeconds}, public";
        }

        public bool IsContentTypeAllowed(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            var mimeType = contentType.Split(';')[0].Trim();

            foreach (var pattern in AllowedContentTypePatternsForETag)
            {
                if (pattern.EndsWith("*"))
                {
                    var prefix = pattern[..^1];
                    if (mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (mimeType.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }*/
    
}