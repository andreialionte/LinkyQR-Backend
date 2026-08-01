// using Delta;  // COMMENTED OUT: Incompatible with cache middleware - see middleware section for details

using System;
using Linky.DataLayer;
using Linky.IRepository;
using Linky.IService;
using Linky.Jobs;
using Linky.Mappers;
using Linky.Middlewares;
using Linky.Middlewares.Linky.Middleware;
using Linky.Repository;
using Linky.Service;
using Linky.Utils;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ;
using TickerQ.Caching.StackExchangeRedis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using UiPath.Caching;
using UiPath.Caching.CloudEvents;
using UiPath.Caching.Config;
using UiPath.Caching.Polly;

namespace Linky
{
    public class Program
    {
        public static void Main(string[] args)
        {
            static bool IsPlaceholderValue(string? value)
            {
                return !string.IsNullOrWhiteSpace(value)
                       && value.StartsWith("${", StringComparison.Ordinal)
                       && value.EndsWith("}", StringComparison.Ordinal);
            }

            static string? FirstRealValue(params string?[] values)
            {
                foreach (var value in values)
                {
                    if (string.IsNullOrWhiteSpace(value) || IsPlaceholderValue(value))
                    {
                        continue;
                    }

                    return value;
                }

                return null;
            }

            // ensure thread‑pool has a reasonable floor in case of sudden load spikes
            // only bump if current min is lower to avoid wasting threads on small machines
            // use a conservative, machine-scaled minimum instead of a fixed 200 which
            // can waste resources on small containers. Tune only after measuring.
            ThreadPool.GetMinThreads(out var wt, out var io);
            var targetMinThreads = Math.Max(Environment.ProcessorCount * 2, 16);
            if (wt < targetMinThreads || io < targetMinThreads)
            {
                ThreadPool.SetMinThreads(workerThreads: targetMinThreads, completionPortThreads: targetMinThreads);
            }

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddResponseCaching(options =>
            {
                options.UseCaseSensitivePaths = false;
                // Increase from 1 KB to 64 KB so typical JSON responses can be cached.
                options.MaximumBodySize = 64 * 1024; // 64 KB
            });

            // memory cache used by custom ETag middleware
            builder.Services.AddMemoryCache();

            builder.Services.AddScoped<DapperDbContext>();
            var dbConnection = FirstRealValue(
                Environment.GetEnvironmentVariable("DB_CONNECTION_STRING"),
                builder.Configuration.GetConnectionString("DefaultConnection"));

            builder.Services.AddDbContext<DataContextEf>(options =>
                options.UseNpgsql(dbConnection));


            builder.Services.AddScoped<IActiveVisitorRepository, ActiveVisitorRepository>();
            builder.Services.AddScoped<IURLShortenerRepository, URLShortenerRepository>();
            builder.Services.AddScoped<IVisitorRepository, VisitorRepository>();
            builder.Services.AddScoped<IVisitorStatsRepository, VisitorStatsRepository>();
            builder.Services.AddScoped<IQRCodeRepository, QRCodeRepository>();

            // Register Mapperly mappers
            builder.Services.AddSingleton<VisitorMapper>();
            builder.Services.AddSingleton<VisitorStatsMapper>();
            builder.Services.AddSingleton<QRCodeMapper>();
            builder.Services.AddSingleton<URLShortenerMapper>();
            builder.Services.AddSingleton<ActiveVisitorMapper>();

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IClientIp, ClientIp>();

            builder.Services.AddSingleton<IGeoIPService, GeoIpService>();
            builder.Services.AddSingleton<ICacheService, CacheService>();

            builder.Services.AddHealthChecks()
                .AddCheck<UptimePercentageHealthCheck>("uptimeCheck", tags: new[] { "uptime" });

            builder.Services.AddScoped<ActiveVisitorJob>();
            builder.Services.AddScoped<AggregateVisitorStats>();

            builder.Services.AddCaching(builder.Configuration.GetSection("Caching"), cachingBuilder =>
                cachingBuilder
                    .AddRedisConnection(connectionOptions =>
                    {
                        var connectionString = FirstRealValue(
                            Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"),
                            builder.Configuration.GetConnectionString("Valkey"),
                            builder.Configuration["Caching:Connections:Redis:ConnectionString"])
                            ?? "localhost:6379";

                        connectionOptions.ConnectionString = connectionString;
                        connectionOptions.AbortOnConnectFail = false;
                        connectionOptions.WarmUpOnStart = false;
                    })
                    .AddBroadcast()
                    .AddRedis()
                    .AddInMemoryRedis()
                    .AddMemory()
                    .AddLocalLock()
                    .AddRedisDistributedLock()
                    .AddResilienceStrategies()
                    .AddCloudEvents(),
                options =>
                {
                    var section = builder.Configuration.GetSection("Caching");
                    section.Bind(options);
                    options.AppShortName = FirstRealValue(
                        options.AppShortName,
                        Environment.GetEnvironmentVariable("CACHING_APP_SHORT_NAME"),
                        builder.Environment.ApplicationName,
                        "linky");
                });

            builder.Services.AddTickerQ(options =>
            {
                options.ConfigureScheduler(schedulerOptions =>
                {
                    schedulerOptions.MaxConcurrency = Environment.ProcessorCount;
                    schedulerOptions.NodeIdentifier = "linky-node-01";
                });

                var redisConnectionString = FirstRealValue(
                    Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"),
                    builder.Configuration.GetConnectionString("Valkey"),
                    "localhost:6379");

                if (!string.IsNullOrEmpty(redisConnectionString))
                {
                    if (!redisConnectionString.Contains("abortConnect"))
                    {
                        redisConnectionString += ",abortConnect=false";
                    }

                    options.AddStackExchangeRedis(redisOptions =>
                    {
                        redisOptions.Configuration = redisConnectionString;
                        redisOptions.InstanceName = "tickerq:";
                        redisOptions.NodeHeartbeatInterval = TimeSpan.FromMinutes(1);
                    });
                }

                options.AddDashboard(dashboardOptions =>
                {
                    dashboardOptions.SetBasePath("/tickerq/dashboard");
                    dashboardOptions.WithBasicAuth("admin", "admin123");
                });
            });
            
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
                options.AddServerHeader = true;

                options.ConfigureHttpsDefaults(httpsOptions =>
                {
                    httpsOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                });

                options.ListenAnyIP(5000, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;

                    listenOptions.UseHttps(httpsOptions =>
                    {
                        httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                        {
                            if (name != null && name.Equals("api.linkyqr.com", StringComparison.OrdinalIgnoreCase))
                            {
                                return new X509Certificate2("/app/api.pfx", "parola_ta");
                            }

                            return new X509Certificate2("/app/linkyqr.pfx", "parola_ta");
                        };
                    });
                });
            });



            builder.Services.AddSignalR();



            //builder.Services.AddReverseProxy()
            //    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

            builder.Services.AddResponseCompression(responseCompressionOptions =>
            {
                responseCompressionOptions.EnableForHttps = true;
                responseCompressionOptions.Providers.Add<BrotliCompressionProvider>();
                responseCompressionOptions.Providers.Add<GzipCompressionProvider>();
            });

            // Brotli compression: prefer lower-latency compression for API responses
            builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            // Gzip to fastest for low-latency fallback
            builder.Services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            builder.Services.AddSwaggerGen();

            builder.Services.AddCors(opts =>
            {
                opts.AddPolicy("main", opts =>
                {
                    opts.WithOrigins("https://linkyqr.com", "https://www.linkyqr.com", "https://linky-qr-frontend.vercel.app/") // your Angular app
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .AllowCredentials()
                                .SetPreflightMaxAge(TimeSpan.FromHours(1));  // PRF
                    // important for SignalR
                });
            });

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContextEf>();
                try
                {
                    dbContext.Database.Migrate();
                    Console.WriteLine("Database migrations applied successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying migrations: {ex.Message}");

                }
            }

            // TickerQ is initialized automatically via builder.Services.AddTickerQ()
            // Jobs are discovered and scheduled based on [TickerFunction] attributes

            app.UseCors("main");

            // Initialize TickerQ scheduler and job execution
            app.UseTickerQ();

            // Security Headers - must be early in pipeline
            var policyCollection = new HeaderPolicyCollection()
                .AddFrameOptionsDeny()
                .AddContentTypeOptionsNoSniff()
                .AddStrictTransportSecurityMaxAgeIncludeSubDomains(maxAgeInSeconds: 31536000)
                .AddReferrerPolicyNoReferrer()
                .AddContentSecurityPolicy(builder =>
                {
                    builder.AddScriptSrc().Self();
                    builder.AddObjectSrc().Self();
                    builder.AddBlockAllMixedContent();
                    builder.AddUpgradeInsecureRequests();
                })
                .AddPermissionsPolicy(builder =>
                {
                    builder.AddCamera().None();
                    builder.AddMicrophone().None();
                    builder.AddGeolocation().Self();
                    builder.AddCustomFeature("usb").None();
                })
                .AddCrossOriginOpenerPolicy(x => x.SameOrigin())
                .AddCustomHeader("Strict-Transport-Security", "max-age=31536000; includeSubDomains")
                .AddCustomHeader("X-XSS-Protection", "0")
                .AddCustomHeader("X-Permitted-Cross-Domain-Policies", "none")
                .AddCustomHeader("Cross-Origin-Embedder-Policy", "require-corp")
                .AddCustomHeader("Cross-Origin-Resource-Policy", "same-origin");
            // REMOVED: Cache-Control from security headers
            // Let the ETag middleware and response caching middleware handle Cache-Control instead

            app.UseSecurityHeaders(policyCollection);

            app.MapHealthChecks("/health", new HealthCheckOptions  //for uptime_percentage health
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";

                    var checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        data = entry.Value.Data
                    });

                    // Specify JsonSerializerOptions
                    var options = new JsonSerializerOptions
                    {
                        // Compact machine-friendly output in production to save CPU and bandwidth
                        WriteIndented = false
                    };

                    string result = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        status = report.Status.ToString(),
                        checks = checks
                    }, options);

                    await context.Response.WriteAsync(result);
                }
            });


            //app.MapReverseProxy();

            app.MapHub<ActiveVisitorsHub>("/ActiveVisitorsHub");



            // IMPORTANT: Middleware Pipeline Order Matters!
            // 
            // CacheETagMiddleware - Adds ETag support for FusionCache responses (L1/L2)
            //   - Generates ETags for data from cache (RAM/Redis)
            //   - Returns 304 Not Modified when ETag matches
            //   - Works with both cached and non-cached responses
            // 
            app.UseMiddleware<CacheETagMiddleware>();

            // Delta.EF - COMMENTED OUT: Incompatible with FusionCache
            //   - Delta.EF can only generate consistent ETags for data returned DIRECTLY from EF Core queries
            //   - When using FusionCache (return Ok(cached)), data bypasses EF Core tracking
            //   - This causes Delta.EF to generate different ETags for identical content
            //   - Result: 304 Not Modified never works because ETags don't match
            // 
            // Delta.EF is designed for:
            //   - Direct DB queries without manual caching layers
            //   - PostgreSQL change tracking for automatic invalidation
            //   - Apps that don't use FusionCache/Redis/in-memory caching in controllers
            // 
            // Read more: https://github.com/SimonCropp/Delta
            // 
            // app.UseDelta<DataContextEf>();

            // Removed middleware that sets Cache-Control: no-cache when ETag is present
            // This was preventing browsers from caching responses and sending If-None-Match for 304 Not Modified
            // app.Use(async (context, next) =>
            // {
            //     context.Response.OnStarting(() =>
            //     {
            //         if (context.Response.Headers.ContainsKey("ETag"))
            //         {
            //             context.Response.Headers["Cache-Control"] = "no-cache";
            //         }
            //         return Task.CompletedTask;
            //     });

            //     await next();
            // });
            app.UseMiddleware<VisitorIdMiddleware>();

            app.UseResponseCaching();

            // REMOVED: Old Cache-Control fallback middleware
            // Previously set "public, max-age=0, must-revalidate" for GET 200 responses
            // without Cache-Control. This is now dead code because CacheETagMiddleware
            // (which runs earlier in the pipeline) sets Cloudflare-style Cache-Control
            // on ALL 200 GET responses. Keeping it would just waste CPU on a check
            // whose header is always overwritten.

            //app.UseRateLimiter();


            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwagger();
                app.UseSwaggerUI();
            } // PROD
             else
             {
                 app.UseHsts();
             }


            app.UseHttpsRedirection();



            app.UseAuthorization();

            app.UseResponseCompression(); // early as possible for builder.Services.AddResponseCompression()
            app.MapControllers();

            app.Run();
        }
    }
}

