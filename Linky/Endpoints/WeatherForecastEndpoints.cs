using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Linky;

namespace Linky.Endpoints
{
    /// <summary>
    /// Minimal API equivalent of Controllers/WeatherForecastController.cs (the default template controller).
    /// </summary>
    public static class WeatherForecastEndpoints
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        public static IEndpointRouteBuilder MapWeatherForecastEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/WeatherForecast", (ILogger<Program> logger) =>
            {
                return Enumerable.Range(1, 5).Select(index => new WeatherForecast
                {
                    Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    TemperatureC = Random.Shared.Next(-20, 55),
                    Summary = Summaries[Random.Shared.Next(Summaries.Length)]
                })
                .ToArray();
            })
            .WithName("GetWeatherForecast")
            .WithTags("WeatherForecast");

            return app;
        }
    }
}


