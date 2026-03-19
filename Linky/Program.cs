// using Delta;  // COMMENTED OUT: Incompatible with FusionCache - see middleware section for details
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
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using System.IO.Compression;
using System.Text.Json;
using TickerQ;
using TickerQ.Caching.StackExchangeRedis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace Linky
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // ensure thread‑pool has a reasonable floor in case of sudden load spikes
            // only bump if current min is lower to avoid wasting threads on small machines
            ThreadPool.GetMinThreads(out var wt, out var io);
            if (wt < 200 || io < 200)
            {
                ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
            }

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddResponseCaching(options =>
            {
                options.UseCaseSensitivePaths = false;
                options.MaximumBodySize = 1024;
            });

            // memory cache used by custom ETag middleware
            builder.Services.AddMemoryCache();

            builder.Services.AddScoped<DapperDbContext>();
            var dbConnection = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

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

            builder.Services.AddHttpContextAccessor(); //for ips etc i thhink
            builder.Services.AddScoped<IClientIp, ClientIp>();

            builder.Services.AddSingleton<IGeoIPService, GeoIpService>();
            builder.Services.AddSingleton<ICacheService, CacheService>(); //sau singleton trb sa inteleg bussiness logic-ul la app

            builder.Services.AddHealthChecks()
                .AddCheck<UptimePercentageHealthCheck>("uptimeCheck", tags: new[] { "uptime" });

            //rate limit by ips 15 req per min
            //builder.Services.AddRateLimiter(options =>
            //{
            //    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            //        RateLimitPartition.GetFixedWindowLimiter(
            //            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            //            factory: _ => new FixedWindowRateLimiterOptions
            //            {
            //                PermitLimit = 15,
            //                Window = TimeSpan.FromSeconds(45),
            //                QueueLimit = 0,
            //                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            //            }));

            //    options.OnRejected = (context, cancellationToken) =>
            //    {
            //        context.HttpContext.Response.StatusCode = 429;
            //        context.HttpContext.Response.Headers["Retry-After"] = "60";
            //        return ValueTask.CompletedTask; // <-- Use ValueTask instead of Task
            //    };
            //});

            //builder.Services.AddNatsServices(builder.Configuration);



            builder.Services.AddScoped<ActiveVisitorJob>();
            builder.Services.AddScoped<AggregateVisitorStats>();

            // Register TickerQ - jobs auto-discovered via [TickerFunction] attributes
            builder.Services.AddTickerQ(options =>
            {
                options.ConfigureScheduler(schedulerOptions =>
                {
                    schedulerOptions.MaxConcurrency = Environment.ProcessorCount;
                    schedulerOptions.NodeIdentifier = "linky-node-01";
                });

                var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
                    ?? builder.Configuration.GetConnectionString("Valkey")
                    ?? "localhost:6379";

                if (!string.IsNullOrEmpty(redisConnectionString))
                {
                    options.AddStackExchangeRedis(redisOptions =>
                    {
                        redisOptions.Configuration = redisConnectionString;
                        redisOptions.InstanceName = "tickerq:";
                        redisOptions.NodeHeartbeatInterval = TimeSpan.FromMinutes(1);
                    });
                }

                // Add Dashboard
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
                options.ListenAnyIP(5000, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                });
            });


            builder.Services.AddSignalR();



            //builder.Services.AddReverseProxy()
            //    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));


            // builder.Services.AddMemoryCache();



            builder.Services.AddFusionCache()
                .WithSerializer(new FusionCacheCysharpMemoryPackSerializer())
                .WithDistributedCache(
                    new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache(
                        new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions
                        {
                            ConnectionMultiplexerFactory = async () =>
                            {
                                var connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("Valkey");

                                // Dacă nu există, returnează null și FusionCache va folosi doar Memory
                                if (string.IsNullOrEmpty(connectionString))
                                    return null;

                                return await ConnectionMultiplexer.ConnectAsync(
                                    connectionString,
                                    config =>
                                    {
                                        config.AbortOnConnectFail = false;
                                        config.ConnectTimeout = 5000;
                                        config.ReconnectRetryPolicy = new LinearRetry(5000);
                                        config.ConnectRetry = 5;
                                    }
                                );
                            }
                        }
                    )
                )
                // ȘTERGE complet .WithBackplane() - nu-l folosești!
                .WithOptions(options =>
                {
                    options.DefaultEntryOptions = new FusionCacheEntryOptions
                    {
                        Duration = TimeSpan.FromMinutes(2),
                        Priority = CacheItemPriority.High,
                        DistributedCacheDuration = TimeSpan.FromHours(1),

                        // Fail-Safe: cache-ul funcționează chiar dacă DB/Redis cad
                        IsFailSafeEnabled = true,
                        FailSafeMaxDuration = TimeSpan.FromHours(6),
                        FailSafeThrottleDuration = TimeSpan.FromSeconds(2),

                        // Performance: operații async în background
                        AllowBackgroundDistributedCacheOperations = true,
                        SkipDistributedCacheReadWhenStale = true,

                        // Anti cache-stampede: refresh înainte de expirare
                        EagerRefreshThreshold = 0.8f,  // Refresh la 80% din Duration (1.6 min)

                        // Timeouts pentru factory (DB queries)
                        FactorySoftTimeout = TimeSpan.FromMilliseconds(500),  // soft timeout
                        FactoryHardTimeout = TimeSpan.FromSeconds(3),         // hard timeout
                        AllowTimedOutFactoryBackgroundCompletion = true,      // continuă în background

                        // Jitter pentru a distribui load-ul
                        JitterMaxDuration = TimeSpan.FromSeconds(10)
                    };
                });

            builder.Services.AddScoped<ICacheService, CacheService>();


            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;

                // Brotli and Gzip providers
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();

                // compress responses larger than 1 KB avoid CPU overhead
                //options.MinimumResponseSizeBytes = 1024;
            });

            // Brotli to optimal for better compression (smaller payloads)
            builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
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
                        WriteIndented = true
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
            // else
            // {
            //     app.UseHsts();
            // }




            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.UseResponseCompression(); // early as possible for builder.Services.AddResponseCompression()
            app.MapControllers();

            app.Run();
        }
    }
}

