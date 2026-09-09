using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Linky.Endpoints
{
    /// <summary>
    /// Minimal API equivalent of Controllers/DefaultController.cs.
    /// Redirects bare "linkyqr.com" requests to "www.linkyqr.com".
    /// </summary>
    public static class DefaultEndpoints
    {
        public static IEndpointRouteBuilder MapDefaultEndpoints(this IEndpointRouteBuilder app)
        {
            // Original: [EnableRateLimiting("public-high-volume-api")] on the controller,
            // [HttpGet("/")] (absolute route) on the action.
            app.MapGet("/", () => Results.Redirect("https://www.linkyqr.com", permanent: true))
                .RequireRateLimiting("public-high-volume-api")
                .WithTags("Default");

            return app;
        }
    }
}


