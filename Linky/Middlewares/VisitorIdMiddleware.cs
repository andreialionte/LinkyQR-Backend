namespace Linky.Middlewares.Linky.Middleware
{
    public class VisitorIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<VisitorIdMiddleware> _logger;

        public VisitorIdMiddleware(RequestDelegate next, ILogger<VisitorIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path;
            _logger.LogInformation($"[MIDDLEWARE] Request to: {path}");

            // Verifică toate cookies primite
            _logger.LogInformation($"[MIDDLEWARE] Cookies received: {string.Join(", ", context.Request.Cookies.Select(c => $"{c.Key}={c.Value}"))}");

            if (!context.Request.Cookies.TryGetValue("VisitorId", out var existingVisitorId) ||
                string.IsNullOrEmpty(existingVisitorId))
            {
                var visitorId = Guid.NewGuid().ToString();
                _logger.LogWarning($"[MIDDLEWARE] NO VisitorId cookie found! Creating new: {visitorId}");

                var cookieOptions = new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = false,
                    Secure = context.Request.IsHttps, // Auto-detect
                    SameSite = context.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
                    Path = "/",
                    IsEssential = true
                };

                context.Response.Cookies.Append("VisitorId", visitorId, cookieOptions);
                context.Items["VisitorId"] = visitorId;

                _logger.LogInformation($"[MIDDLEWARE] Cookie set with options: Secure={cookieOptions.Secure}, SameSite={cookieOptions.SameSite}");
            }
            else
            {
                _logger.LogInformation($"[MIDDLEWARE] Found existing VisitorId: {existingVisitorId}");
                context.Items["VisitorId"] = existingVisitorId;
            }

            await _next(context);
        }
    }
}