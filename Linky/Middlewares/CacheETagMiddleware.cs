using System.Security.Cryptography;
using System.Text;

namespace Linky.Middlewares
{
    /// <summary>
    /// RFC 7232 Compliant ETag Middleware for HTTP Conditional Requests
    /// 
    /// IMPLEMENTS: https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/Conditional_requests
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// SAFE METHODS (GET, HEAD) - FULL IMPLEMENTATION ✅
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// Automatically handles:
    ///   ✅ ETag generation (SHA256, weak ETags with W/)
    ///   ✅ Last-Modified header generation
    ///   ✅ If-None-Match validation → 304 Not Modified
    ///   ✅ If-Modified-Since validation → 304 Not Modified (fallback for old browsers)
    ///   ✅ Proper header cleanup for 304 (Content-Length, Content-Encoding, Transfer-Encoding)
    /// 
    /// Use cases:
    ///   • Cache validation (browser/CDN checks if content changed)
    ///   • Bandwidth savings (304 = ~50 bytes vs KB of JSON)
    ///   • Mobile data savings (97% bandwidth reduction on cache hits)
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
            // Only process GET and HEAD requests for full RFC 7232 conditional request handling
            // (ETag generation, Last-Modified, If-None-Match/If-Modified-Since validation, 304 responses)
            if (context.Request.Method != HttpMethods.Get && 
                context.Request.Method != HttpMethods.Head)
            {
                // For PUT/PATCH/DELETE/POST: Controllers must validate If-Match/If-Unmodified-Since manually
                // and return 412 Precondition Failed if needed (see class documentation above)
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
                    DateTime lastModified;
                    
                    if (!context.Response.Headers.ContainsKey("ETag"))
                    {
                        // No ETag from Delta.EF - This is a cached response
                        // Generate ETag from response body for 304 support
                        etag = GenerateETag(responseBytes);
                        context.Response.Headers.ETag = etag;
                        
                        // Generate Last-Modified header (RFC 7232 requirement)
                        // Use current time as fallback (cache responses don't have original modification time)
                        lastModified = DateTime.UtcNow;
                        context.Response.Headers.LastModified = lastModified.ToString("R"); // RFC 1123 format
                    }
                    else
                    {
                        // ETag already exists from Delta.EF - This is a direct DB query
                        // Reuse Delta's ETag (it's already optimized for PostgreSQL change tracking)
                        etag = context.Response.Headers.ETag.ToString();
                        
                        // Try to parse Last-Modified if Delta.EF set it
                        if (context.Response.Headers.TryGetValue("Last-Modified", out var lastModifiedValue) &&
                            DateTime.TryParse(lastModifiedValue, out var parsedDate))
                        {
                            lastModified = parsedDate;
                        }
                        else
                        {
                            lastModified = DateTime.UtcNow;
                            context.Response.Headers.LastModified = lastModified.ToString("R");
                        }
                    }

                    // ==========================================
                    // RFC 7232 CONDITIONAL REQUEST VALIDATION
                    // ==========================================
                    
                    // 1. Check If-None-Match (ETag-based validation - preferred method)
                    // Per RFC 7232: If-None-Match can contain multiple ETags comma-separated
                    if (context.Request.Headers.TryGetValue("If-None-Match", out var incomingETags))
                    {
                        // Split multiple ETags and check if current ETag matches any
                        var etagList = incomingETags.ToString()
                            .Split(',')
                            .Select(e => e.Trim())
                            .ToList();

                        if (etagList.Contains(etag) || etagList.Contains("*"))
                        {
                            // ETags match - Content hasn't changed
                            Return304NotModified(context, originalBodyStream);
                            return;
                        }
                    }
                    
                    // 2. Check If-Modified-Since (Date-based validation - fallback for older browsers)
                    // Per RFC 7232: Only used if If-None-Match is not present
                    // This is important for browsers that don't fully support ETags
                    else if (context.Request.Headers.TryGetValue("If-Modified-Since", out var ifModifiedSinceValue) &&
                             DateTime.TryParse(ifModifiedSinceValue, out var ifModifiedSince))
                    {
                        // Compare dates: if resource wasn't modified since client's cached date
                        // Round to seconds (HTTP dates don't include milliseconds)
                        if (lastModified.AddMilliseconds(-lastModified.Millisecond) <= ifModifiedSince)
                        {
                            // Resource not modified since client's cache date
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
        /// Returns HTTP 304 Not Modified response per RFC 7232.
        /// Removes content-related headers and ensures empty body (0 bytes).
        /// </summary>
        private static void Return304NotModified(HttpContext context, Stream originalBodyStream)
        {
            // Return 304 Not Modified (saves bandwidth, ultra-fast response)
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.Body = originalBodyStream;
            
            // CRITICAL: 304 responses MUST have empty body (0 bytes)
            // Set ContentLength to 0 to ensure no body is sent
            context.Response.ContentLength = 0;
            
            // Per RFC 7232: Remove content-related headers for 304
            // These MUST be removed as they describe the message body, which is not sent
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Transfer-Encoding");
            
            // Headers that MUST remain (auto-set by ASP.NET Core):
            // - Date (server timestamp)
            // - ETag (validation identifier)
            // - Last-Modified (resource modification time)
            // - Cache-Control (caching directives)
            // - Vary (content negotiation)
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
