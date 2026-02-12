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
            if (!context.Request.Cookies.TryGetValue("VisitorId", out var existingVisitorId) ||
                string.IsNullOrEmpty(existingVisitorId))
            {
                var visitorId = Guid.NewGuid().ToString();
                //  deoarece visitorId se tot restarta la fiecare refresh
                var cookieOptions = new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = false,
                    Secure = true,
                    SameSite = SameSiteMode.None,
                    Path = "/",
                    IsEssential = true
                };

                context.Response.Cookies.Append("VisitorId", visitorId, cookieOptions);
                context.Items["VisitorId"] = visitorId;
            }
            else
            {
                context.Items["VisitorId"] = existingVisitorId;
            }

            await _next(context);
        }
    }
}