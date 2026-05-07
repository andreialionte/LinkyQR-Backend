using System;

namespace Linky.Middlewares
{
    /// <summary>
    /// Configuration options for CacheETagMiddleware.
    /// 
    /// Register in Program.cs:
    ///   builder.Services.Configure<CacheETagOptions>(options =>
    ///   {
    ///       options.MaxResponseSizeForETag = 5 * 1024 * 1024; // 5 MB
    ///       options.MaxAgeSeconds = 300; // 5 minutes
    ///       options.StaleWhileRevalidateSeconds = 10800; // 3 hours
    ///       options.StaleIfErrorSeconds = 10800; // 3 hours
    ///       options.EnableLogging = true;
    ///       options.EnableMetrics = true;
    ///   });
    /// </summary>
    public class CacheETagOptions
    {
        /// <summary>
        /// Maximum response body size (in bytes) to process for ETag generation.
        /// Responses larger than this are skipped (no ETag added).
        /// Default: 5 MB (5,242,880 bytes)
        /// 
        /// Rationale: Large responses (files, videos, streams) benefit less from ETags.
        /// Hashing multi-MB responses is CPU-intensive.
        /// </summary>
        public long MaxResponseSizeForETag { get; set; } = 5 * 1024 * 1024;

        /// <summary>
        /// Cache-Control max-age value in seconds.
        /// Browser serves from disk cache with ZERO network requests during this window.
        /// Default: 300 (5 minutes)
        /// </summary>
        public int MaxAgeSeconds { get; set; } = 300;

        /// <summary>
        /// Cache-Control stale-while-revalidate value in seconds (RFC 5861).
        /// After max-age expires, browser immediately serves stale cached response
        /// AND fires a background revalidation request (If-None-Match → 304/200).
        /// Default: 10800 (3 hours)
        /// </summary>
        public int StaleWhileRevalidateSeconds { get; set; } = 10800;

        /// <summary>
        /// Cache-Control stale-if-error value in seconds (RFC 5861).
        /// If origin returns 5xx or is unreachable, browser serves stale content
        /// instead of showing an error page.
        /// Default: 10800 (3 hours)
        /// </summary>
        public int StaleIfErrorSeconds { get; set; } = 10800;

        /// <summary>
        /// Content-Type MIME patterns to process for ETag generation.
        /// Responses NOT matching these patterns are skipped (no ETag added).
        /// Default: JSON, XML, text, JSONAPI, etc.
        /// 
        /// Examples of patterns:
        ///   "application/json" → matches application/json and application/json; charset=utf-8
        ///   "text/" → matches all text/* types (text/plain, text/html, text/csv)
        ///   "application/*+json" → matches application/vnd.api+json, application/hal+json
        /// 
        /// Binary formats (images, videos, archives) are excluded by default.
        /// </summary>
        public string[] AllowedContentTypePatternsForETag { get; set; } = new[]
        {
            "application/json",           // API responses
            "application/ld+json",        // JSON-LD
            "application/*+json",         // HAL+JSON, Vnd+JSON, etc.
            "application/xml",            // XML APIs
            "application/*+xml",          // SVG+XML, Atom+XML, etc.
            "text/plain",                 // Plain text
            "text/html",                  // HTML (rare for APIs, but OK)
            "text/csv",                   // CSV exports
            "application/problem+json",   // RFC 7807 Problem Details
        };

        /// <summary>
        /// Enable debug-level logging for ETag operations.
        /// Logs: ETag generation, 304 responses, size skips, content-type skips.
        /// Default: false
        /// 
        /// Set to true in development/staging for diagnostics.
        /// Keep false in production (reduces log volume).
        /// </summary>
        public bool EnableLogging { get; set; } = false;

        /// <summary>
        /// Enable optional metrics collection (request counts, 304 ratio, etc.).
        /// Requires metrics provider (e.g., OpenTelemetry).
        /// Default: false
        /// </summary>
        public bool EnableMetrics { get; set; } = false;

        /// <summary>
        /// Validate and build the final Cache-Control header value.
        /// Combines max-age + stale-while-revalidate + stale-if-error + public.
        /// </summary>
        public string BuildCacheControlHeader()
        {
            return $"max-age={MaxAgeSeconds}, stale-while-revalidate={StaleWhileRevalidateSeconds}, stale-if-error={StaleIfErrorSeconds}, public";
        }

        /// <summary>
        /// Check if the given Content-Type should be processed for ETag generation.
        /// Performs prefix matching against AllowedContentTypePatternsForETag.
        /// 
        /// Examples:
        ///   IsContentTypeAllowed("application/json; charset=utf-8")
        ///   → checks if "application/json" matches any pattern in the list
        ///   → true (matches "application/json" exactly)
        /// 
        ///   IsContentTypeAllowed("image/png")
        ///   → checks all patterns
        ///   → false (no pattern matches "image/png")
        /// </summary>
        public bool IsContentTypeAllowed(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            // Extract MIME type without charset/boundary parameters
            var mimeType = contentType.Split(';')[0].Trim();

            // Check against allowed patterns (prefix + wildcard matching)
            foreach (var pattern in AllowedContentTypePatternsForETag)
            {
                if (pattern.EndsWith("*"))
                {
                    // Pattern like "application/*+json" or "text/*"
                    var prefix = pattern[..^1]; // Remove trailing *
                    if (mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    // Exact match like "application/json"
                    if (mimeType.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}

