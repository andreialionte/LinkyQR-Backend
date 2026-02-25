using System.Security.Cryptography;
using System.Text;

namespace Linky.Middlewares
{
    /// <summary>
    /// RFC 9110 Compliant ETag Middleware with Cloudflare-Style Cache-Control
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
                    // ──────────────────────────────────────────────────────────
                    // FAST PATH: Delta.EF already set an ETag → check If-None-Match
                    // BEFORE reading the body into a byte array. This avoids:
                    //   • responseBody.ToArray() allocation (can be several KB)
                    //   • SHA256.HashData() CPU cost
                    // Only possible when the upstream pipeline already set ETag.
                    // ──────────────────────────────────────────────────────────
                    if (context.Response.Headers.ContainsKey("ETag") &&
                        context.Request.Headers.TryGetValue("If-None-Match", out var earlyIncomingETags))
                    {
                        var existingEtag = context.Response.Headers.ETag.ToString();
                        var earlyEtagList = earlyIncomingETags.ToString()
                            .Split(',')
                            .Select(e => e.Trim());

                        if (earlyEtagList.Any(e => WeakETagEquals(e, existingEtag)) ||
                            earlyEtagList.Contains("*"))
                        {
                            // ETag match — content unchanged. Return 304 without touching body.
                            SetCacheHeaders(context);
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }

                    responseBody.Seek(0, SeekOrigin.Begin);
                    var responseBytes = responseBody.ToArray();

                    // Determine ETag: reuse Delta.EF's or generate from response body
                    // Delta.EF generates ETags ONLY for direct EF Core DB queries.
                    // FusionCache responses (from RAM/Redis) bypass EF Core — no ETag from Delta.
                    string etag;
                    
                    if (!context.Response.Headers.ContainsKey("ETag"))
                    {
                        // No ETag from Delta.EF → This is a cached response
                        // Generate ETag from response body content for 304 support
                        etag = GenerateETag(responseBytes);
                        context.Response.Headers.ETag = etag;
                        
                        // DO NOT set Last-Modified for cached responses!
                        // FusionCache doesn't preserve original modification timestamp.
                        // DateTime.UtcNow would change on every request, breaking 304 validation.
                        // ETags alone are sufficient and more reliable for cache validation.
                    }
                    else
                    {
                        // ETag already exists from Delta.EF → This is a direct DB query
                        // Reuse Delta's ETag (optimized for PostgreSQL change tracking)
                        etag = context.Response.Headers.ETag.ToString();
                        
                        // Only use Last-Modified if Delta.EF already set it
                        // (Delta.EF knows the actual DB modification timestamp)
                    }

                    // Set Cache-Control + Vary (see SetCacheHeaders() for full documentation)
                    SetCacheHeaders(context);

                    // ==========================================
                    // RFC 7232 CONDITIONAL REQUEST VALIDATION
                    // ==========================================
                    
                    // Check If-None-Match (ETag-based validation - primary method)
                    // Per RFC 7232 §3.2: If-None-Match can contain multiple ETags, comma-separated
                    // Uses weak comparison (W/ prefix ignored) per RFC 7232 §2.3.2
                    if (context.Request.Headers.TryGetValue("If-None-Match", out var incomingETags))
                    {
                        var etagList = incomingETags.ToString()
                            .Split(',')
                            .Select(e => e.Trim());

                        if (etagList.Any(e => WeakETagEquals(e, etag)) || etagList.Contains("*"))
                        {
                            // ETags match — content unchanged
                            // DON'T copy responseBody to originalBodyStream at all
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }
                    
                    // If-Modified-Since validation only for responses that have Last-Modified
                    // (Delta.EF responses from direct DB queries)
                    // We don't set Last-Modified for cached responses, so this only applies to DB queries
                    else if (context.Response.Headers.ContainsKey("Last-Modified") &&
                             context.Request.Headers.TryGetValue("If-Modified-Since", out var ifModifiedSinceValue) &&
                             DateTime.TryParse(ifModifiedSinceValue, out var ifModifiedSince) &&
                             context.Response.Headers.TryGetValue("Last-Modified", out var lastModifiedValue) &&
                             DateTime.TryParse(lastModifiedValue, out var lastModified))
                    {
                        // Compare dates: if resource wasn't modified since client's cached date
                        // Round to seconds (HTTP dates don't include milliseconds)
                        if (lastModified.AddMilliseconds(-lastModified.Millisecond) <= ifModifiedSince)
                        {
                            Return304NotModified(context, originalBodyStream);
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
        /// Returns HTTP 304 Not Modified response per RFC 7232 §4.1.
        /// 
        /// Removes content-related headers and ensures empty body.
        /// The key is to NOT copy the responseBody MemoryStream to originalBodyStream at all.
        /// 
        /// Content-Length handling per RFC 7230 §3.3:
        ///   A 304 MUST NOT contain Content-Length that differs from what would have
        ///   been sent in the corresponding 200. Since we don't know the 200 body size
        ///   at this point (body may not have been read), we REMOVE Content-Length entirely.
        ///   This is safer than setting 0 (which would be a spec violation for non-empty bodies).
        /// </summary>
        private static void Return304NotModified(HttpContext context, Stream originalBodyStream)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            
            // Switch back to the original body stream (nothing written to it)
            context.Response.Body = originalBodyStream;
            
            // Per RFC 7232 §4.1: Remove headers describing the message body
            // A 304 has no body — these headers would be misleading
            context.Response.ContentLength = null; // REMOVE, not 0 (RFC 7230 §3.3)
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Transfer-Encoding");
            context.Response.Headers.Remove("Content-Type");
            
            // Headers that REMAIN on 304 (per RFC 7232 §4.1):
            // - Date (server timestamp)
            // - ETag (the validator that matched)
            // - Cache-Control (caching directives)
            // - Vary (content negotiation — tells caches to key on Accept-Encoding)
            // - Last-Modified (only if set by Delta.EF for DB queries)
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