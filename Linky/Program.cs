// using Delta;
// // COMMENTED OUT: Incompatible with cache middleware - see middleware section for details

using System;
using Linky.DataLayer;
using Linky.Endpoints;
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
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RedisRateLimiting;
using RedisRateLimiting.AspNetCore;
using StackExchange.Redis;
using TickerQ;
using TickerQ.Caching.StackExchangeRedis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using GlideConnectionMultiplexer = Valkey.Glide.ConnectionMultiplexer;

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

            // Valkey.Glide's ConfigurationOptions parser supports a smaller keyword set than
            // StackExchange.Redis. Options like "abortConnect" (which we append/rely on for
            // StackExchange.Redis based clients) throw an ArgumentException when handed to
            // Valkey.Glide.ConfigurationOptions.Parse. Strip any keys Glide doesn't understand
            // before parsing so the same connection string can be shared between both clients.
            static string StripUnsupportedGlideOptions(string connectionString)
            {
                var unsupportedKeys = new[] { "abortConnect" };

                var filteredParts = connectionString
                    .Split(',')
                    .Where(part =>
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length == 0)
                        {
                            return false;
                        }

                        var eqIndex = trimmed.IndexOf('=');
                        if (eqIndex < 0)
                        {
                            // host:port style entries have no '=' and should always be kept
                            return true;
                        }

                        var key = trimmed[..eqIndex].Trim();
                        return !unsupportedKeys.Any(unsupportedKey =>
                            string.Equals(unsupportedKey, key, StringComparison.OrdinalIgnoreCase));
                    });

                return string.Join(",", filteredParts);
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

            // Converted from MVC Controllers to Minimal API Endpoints (see Endpoints/ folder
            // and the app.MapXxxEndpoints() calls below). AddControllers()/MapControllers()
            // are kept here (commented out) for reference/rollback.
            // builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            // ---- Rate Limiting ----
            // Single policy ("spam-api") applied to every controller.
            // Backed by Redis so the limit is enforced correctly across multiple instances.
            // Partitioned per client IP (via IClientIp) so one abusive caller only burns their own bucket.
            // If Redis is unreachable, falls back to an in-memory per-instance bucket
            // (fail-open on a degraded/uncertain connection so a Redis blip doesn't 500 the whole API).

            var rateLimiterRedisConnectionString = FirstRealValue(
                Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"),
                builder.Configuration.GetConnectionString("Valkey"),
                builder.Configuration["Caching:Connections:Redis:ConnectionString"])
                ?? "localhost:6379";

            var rateLimiterRedisOptions = StackExchange.Redis.ConfigurationOptions.Parse(rateLimiterRedisConnectionString);
            rateLimiterRedisOptions.AbortOnConnectFail = false; // don't throw at startup if Redis is briefly unreachable

            var rateLimiterRedis = StackExchange.Redis.ConnectionMultiplexer.Connect(rateLimiterRedisOptions);
            builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(rateLimiterRedis);

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, ct) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString();
                    }

                    context.HttpContext.Response.ContentType = "application/problem+json";
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        title = "Too many requests",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "Rate limit exceeded. Retry after the Retry-After header value."
                    }, ct);
                };

                // Shared helper: Redis-backed token bucket, per-IP, falls back to in-memory if Redis is down.
                RateLimitPartition<string> TokenBucketWithFallback(HttpContext httpContext, int tokenLimit)
                {
                    var clientIp = httpContext.RequestServices
                        .GetRequiredService<IClientIp>()
                        .GetClientIp();

                    try
                    {
                        if (!rateLimiterRedis.IsConnected)
                        {
                            throw new StackExchange.Redis.RedisConnectionException(StackExchange.Redis.ConnectionFailureType.UnableToConnect, "Redis not connected");
                        }

                        return RateLimitPartition.Get(clientIp, key => new RedisTokenBucketRateLimiter<string>(
                            key,
                            new RedisTokenBucketRateLimiterOptions
                            {
                                ConnectionMultiplexerFactory = () => rateLimiterRedis,
                                TokenLimit = tokenLimit,
                                TokensPerPeriod = tokenLimit,
                                ReplenishmentPeriod = TimeSpan.FromMinutes(1)
                            }));
                    }
                    catch
                    {
                        // Redis unreachable - fall back to an in-memory bucket for this instance
                        // so the API stays protected (albeit per-instance) instead of failing the request.
                        return RateLimitPartition.GetTokenBucketLimiter(clientIp, _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = tokenLimit,
                            TokensPerPeriod = tokenLimit,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        });
                    }
                }

                // Default policy - writes, spammable endpoints, anything not explicitly overridden.
                options.AddPolicy("spam-api", httpContext => TokenBucketWithFallback(httpContext, tokenLimit: 30));

                // Generous policy - public redirects/reads (GetByCode, QR scan-and-redirect, etc.)
                // Higher legitimate volume expected here, so a looser cap than the default.
                // One that they role is to get use not so rarely more 
                options.AddPolicy("public-high-volume-api", httpContext => TokenBucketWithFallback(httpContext, tokenLimit: 100));

                // QR image generation - CPU/memory bound (render + optional logo composite).
                // Caps how many can run AT ONCE per IP, not how many per minute.
                options.AddPolicy("qr-image-gen", httpContext =>
                {
                    var clientIp = httpContext.RequestServices
                        .GetRequiredService<IClientIp>()
                        .GetClientIp();

                    try
                    {
                        if (!rateLimiterRedis.IsConnected)
                        {
                            throw new StackExchange.Redis.RedisConnectionException(StackExchange.Redis.ConnectionFailureType.UnableToConnect, "Redis not connected");
                        }

                        return RateLimitPartition.Get(clientIp, key => new RedisConcurrencyRateLimiter<string>(
                            key,
                            new RedisConcurrencyRateLimiterOptions
                            {
                                ConnectionMultiplexerFactory = () => rateLimiterRedis,
                                PermitLimit = 2,
                                QueueLimit = 4
                            }));
                    }
                    catch
                    {
                        return RateLimitPartition.GetConcurrencyLimiter(clientIp, _ => new ConcurrencyLimiterOptions
                        {
                            PermitLimit = 2,
                            QueueLimit = 4,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        });
                    }
                });
            });
            // ---- End Rate Limiting ----

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

            
            var applicationCacheConnectionString = FirstRealValue(
                Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"),
                builder.Configuration.GetConnectionString("Valkey"))
                ?? "localhost:6379";

            var cacheEndpoint = applicationCacheConnectionString
                .Split(',', 2, StringSplitOptions.TrimEntries)[0];
            var cacheEndpointParts = cacheEndpoint.Split(':', 2, StringSplitOptions.TrimEntries);
            var cacheHost = cacheEndpointParts[0];
            var cachePort = cacheEndpointParts.Length == 2 &&
                            ushort.TryParse(cacheEndpointParts[1], out var parsedCachePort)
                ? parsedCachePort
                : (ushort)6379;

            var glideCacheOptions = Valkey.Glide.ConfigurationOptions.Parse(
                StripUnsupportedGlideOptions(applicationCacheConnectionString));
            var glideCacheConnection = GlideConnectionMultiplexer.Connect(glideCacheOptions);
            builder.Services.AddSingleton(glideCacheConnection);

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

                    // listenOptions.UseHttps(httpsOptions =>
                    // {
                    //     httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                    //     {
                    //         if (name != null && name.Equals("api.linkyqr.com", StringComparison.OrdinalIgnoreCase))
                    //         {
                    //             return new X509Certificate2("/app/api.pfx", "parola_ta");
                    //         }

                    //         return new X509Certificate2("/app/linkyqr.pfx", "parola_ta");
                    //     };
                    // });
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

            // Needed so SwaggerGen/Swashbuckle can discover Minimal API endpoints
            // (MVC controllers got this for free from AddControllers(); Minimal APIs need it explicitly).
            builder.Services.AddEndpointsApiExplorer();
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
            app.UseRouting();
            app.UseRateLimiter();      // 429 before doing auth work



            app.UseAuthorization();

            app.UseResponseCompression(); // early as possible for builder.Services.AddResponseCompression()

            // Converted from MVC Controllers to Minimal API Endpoints - see Endpoints/ folder.
            // app.MapControllers();
            app.MapDefaultEndpoints();
            app.MapQRCodeEndpoints();
            app.MapVisitorEndpoints();
            app.MapVisitorStatsEndpoints();
            app.MapWeatherForecastEndpoints();
            // UrlShortener owns the root-level "{code}" catch-all route, so it must be
            // mapped last - otherwise it could shadow more specific routes above.
            app.MapUrlShortenerEndpoints();

            app.Run();
        }
    }
}
