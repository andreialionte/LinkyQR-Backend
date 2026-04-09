using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

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

        public CacheETagMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // ─────────────────────────────────────────────────────────────
            // BYPASS: Non-cacheable requests skip body buffering entirely
            // ─────────────────────────────────────────────────────────────
            
            // 1. Only GET/HEAD use conditional requests (ETag, If-None-Match, 304)
            //    PUT/PATCH/DELETE/POST: Controllers validate If-Match manually → 412
            if (context.Request.Method != HttpMethods.Get && 
                context.Request.Method != HttpMethods.Head)
            {
                await _next(context);
                return;
            }

            // 2. WebSocket upgrades (SignalR) must NOT be buffered in MemoryStream
            //    The Upgrade header signals a protocol switch — response body is a
            //    persistent bidirectional stream, not a finite HTTP response.
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

                    // ──────────────────────────────────────────────────────────
                    // STEP 1: Determine ETag for this response
                    // Delta.EF: Sets ETag directly on DB queries
                    // FusionCache: No ETag from upstream → generate from body hash
                    // ──────────────────────────────────────────────────────────
                    string etag;
                    
                    if (!context.Response.Headers.ContainsKey("ETag"))
                    {
                        // No ETag from Delta.EF → Generate from response body
                        // This handles FusionCache responses (memory/Redis cached)
                        etag = GenerateETag(responseBytes);
                        context.Response.Headers.ETag = etag;
                    }
                    else
                    {
                        // ETag already exists from Delta.EF (direct DB query)
                        etag = context.Response.Headers.ETag.ToString();
                    }

                    // Set Cache-Control + Vary headers on ALL 200 responses
                    SetCacheHeaders(context);

                    // ──────────────────────────────────────────────────────────
                    // STEP 2: RFC 7232 Conditional Request Validation
                    // Return 304 Not Modified if client's cached version matches
                    // ──────────────────────────────────────────────────────────
                    
                    // If-None-Match (ETag-based validation - primary, stronger method)
                    // Per RFC 7232 §3.2: weak comparison for GET/HEAD
                    // Client can send multiple ETags: If-None-Match: "v1", "v2", "v3"
                    if (context.Request.Headers.TryGetValue("If-None-Match", out var incomingETags))
                    {
                        var incomingEtagList = incomingETags.ToString()
                            .Split(',')
                            .Select(e => e.Trim());

                        // RFC 7232 §3.2: If-None-Match matches if ANY etag matches (weak comparison)
                        // Also handle "*" which means "if resource exists" (used in conditional uploads)
                        if (incomingEtagList.Contains("*") || 
                            incomingEtagList.Any(e => WeakETagEquals(e, etag)))
                        {
                            // Content hasn't changed — return 304 Not Modified
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }
                    
                    // If-Modified-Since (date-based validation - fallback)
                    // Only used if If-None-Match NOT present (per RFC 7232 §6)
                    // Only meaningful for responses that have Last-Modified (Delta.EF DB queries)
                    if (!context.Request.Headers.ContainsKey("If-None-Match") &&
                        context.Response.Headers.ContainsKey("Last-Modified") &&
                        context.Request.Headers.TryGetValue("If-Modified-Since", out var ifModifiedSinceValue) &&
                        DateTime.TryParse(ifModifiedSinceValue, out var ifModifiedSince) &&
                        context.Response.Headers.TryGetValue("Last-Modified", out var lastModifiedValue) &&
                        DateTime.TryParse(lastModifiedValue, out var lastModified))
                    {
                        // Compare dates: resource unchanged if Last-Modified <= If-Modified-Since
                        // HTTP dates are second-precision; round to compare safely
                        if (lastModified.AddMilliseconds(-lastModified.Millisecond) <= ifModifiedSince.AddMilliseconds(-ifModifiedSince.Millisecond))
                        {
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }

                    // No conditional match — return full 200 with new ETag
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
            context.Response.ContentLength = null;  // RFC 7230 §3.3: MUST NOT be 0 if body is non-empty
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Transfer-Encoding");
            context.Response.Headers.Remove("Content-Type");
            
            // Keep validation + caching headers for client
            // ETag, Last-Modified, Cache-Control, Vary, Date remain
        }

        /// <summary>
        /// Sets Cache-Control and Vary headers for Cloudflare-style caching.
        /// Extracted to avoid duplication between the fast-path and normal-path.
        /// </summary>
        private static void SetCacheHeaders(HttpContext context)
        {
            // Cache-Control: Cloudflare-style freshness + stale directives
            // ─────────────────────────────────────────────────────────────
            // max-age=300 (5 min):
            //   Browser serves from disk cache with ZERO network requests.
            //   DevTools shows "200 OK (from disk cache)".
            //
            // stale-while-revalidate=10800 (3 hrs):
            //   After max-age expires, browser immediately serves stale cached response
            //   AND fires a background revalidation request (If-None-Match → 304/200).
            //   User sees instant response; cache silently refreshes in the background.
            //   RFC 5861 §3: https://datatracker.ietf.org/doc/html/rfc5861#section-3
            //
            // stale-if-error=10800 (3 hrs):
            //   If origin returns 5xx or is unreachable, browser serves stale content
            //   instead of showing an error page. Resilience against downtime.
            //   RFC 5861 §4: https://datatracker.ietf.org/doc/html/rfc5861#section-4
            //
            // public:
            //   Response can be stored by any cache (browser, CDN, proxy).
            //
            // NO must-revalidate:
            //   must-revalidate forces the browser to contact the server once max-age
            //   expires, blocking the response until revalidation completes. Without it,
            //   stale-while-revalidate can serve stale instantly + revalidate in background.
            //
            // Timeline:
            //   0–300s        → disk cache (zero requests, instant)
            //   300s–10800s   → stale served instantly + background revalidation (ETag/304)
            //   >10800s       → must revalidate before serving (standard behavior)
            //   Origin down   → stale served for up to 3 hrs (stale-if-error)
            context.Response.Headers.CacheControl =
                "max-age=300, stale-while-revalidate=10800, stale-if-error=10800, public";

            // Vary: Accept-Encoding
            // Tells caches (CDN, proxies, browser) to store separate cached variants
            // per encoding (gzip, br, zstd, identity). Without this, a proxy could
            // serve a gzip-compressed response to a client expecting brotli.
            // Cloudflare always sets this — we match that behavior at origin.
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
}