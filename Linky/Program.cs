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
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OwaspHeaders.Core.Extensions;
using Quartz;
using StackExchange.Redis;
using System.Data;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace Linky
{
    public class Program
    {
        public static void Main(string[] args)
        {
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



            var random = new Random();
            int intervalSeconds = random.Next(40, 130); // interval între 20 și 45 secunde

            builder.Services.AddQuartz(q =>
            {
                var jobKey = new JobKey("ActiveVisitorJob");
                q.AddJob<ActiveVisitorJob>(opts => opts.WithIdentity(jobKey));

                q.AddTrigger(opts =>
                    opts.ForJob(jobKey)
                        .WithIdentity("ActiveVisitorJob-trigger")
                        .WithSimpleSchedule(x => x
                            .WithIntervalInSeconds(intervalSeconds)
                            .WithMisfireHandlingInstructionIgnoreMisfires()
                            .RepeatForever()));

                var jobKey2 = new JobKey("AggregateVisitorStatsJob");
                q.AddJob<AggregateVisitorStats>(opts => opts.WithIdentity(jobKey2));

                // Trigger: run weekly at 05:00 Romania time (aggregates previous 05:00 -> current 05:00 window)
                // Resolve Romania timezone in a cross-platform way (Linux: "Europe/Bucharest", Windows: "E. Europe Standard Time")
                TimeZoneInfo romaniaTimeZone;
                try
                {
                    romaniaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
                }
                catch (TimeZoneNotFoundException)
                {
                    try
                    {
                        romaniaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
                    }
                    catch
                    {
                        // Fallback to UTC if Romania TZ cannot be found
                        romaniaTimeZone = TimeZoneInfo.Utc;
                    }
                }

                q.AddTrigger(opts => opts
                    .ForJob(jobKey2)
                    .WithIdentity("AggregateVisitorStatsTrigger")
                    .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Monday, 5, 0)
                        .InTimeZone(romaniaTimeZone)
                        .WithMisfireHandlingInstructionDoNothing()) // Don't run missed schedules on startup
                    );
            });

            //// Adaugă Quartz hosted service
            builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.ListenAnyIP(5000, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                    listenOptions.UseHttps(); 
                    // Disable Kestrel's auto-generated Alt-Svc header (adds only h3=":port"; ma=86400)
                    // so our middleware can set the full custom value with persist=1 and draft versions.
                    // Source: ListenOptions.DisableAltSvcHeader in ASP.NET Core Kestrel source.
                    listenOptions.DisableAltSvcHeader = true;
                });
            });

            int cpuCores = Environment.ProcessorCount;
            int tickerQConcurrency = Math.Max(1, (int)(cpuCores * 0.25));

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



            app.UseCors("main");

            // Advertise HTTP/3 support (QUIC) to clients via Alt-Svc header.
            // Kestrel auto-adds h3=":443" when HTTP/3 is configured - we override it via OnStarting
            // to ensure a single, complete Alt-Svc header.
            //
            // NOTE on draft versions (h3-29, h3-27):
            //   These were QUIC/HTTP3 drafts from mid-2020. As of 2026, no modern browser
            //   negotiates versions below h3-32. They are parsed and silently ignored.
            //   Including them does no harm but provides zero benefit.
            //
            // NOTE on persist=1:
            //   Per RFC 7838 §3.1, persist=1 is a per-alt-value parameter.
            //   Without it, browsers clear cached Alt-Svc entries on network changes (wifi→4G etc.).
            //   With persist=1, the browser keeps the HTTP/3 hint across network changes.
            //   Must be on EACH entry individually to be spec-compliant.
            //
            // ma=86400 = client caches the hint for 24 hours
            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Alt-Svc"] =
                        "h3=\":443\"; ma=86400; persist=1, " +
                        "h3-29=\":443\"; ma=86400; persist=1, " +
                        "h3-27=\":443\"; ma=86400; persist=1";
                    return Task.CompletedTask;
                });
                await next();
            });

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

            // Cache-Control Strategy for ETag-based caching:
            // - public: Allows proxies/CDNs to cache
            // - max-age=0: Browser ALWAYS revalidates with server (sends If-None-Match)
            // - must-revalidate: Forces validation when cache expires
            //
            // FLOW:
            // Request 1: Server returns 200 OK + ETag, browser saves in cache
            // Request 2+: Browser sends If-None-Match -> Server returns 304 Not Modified (minimal bandwidth)
            //
            // RESULT: Always fresh data with bandwidth savings via 304 responses
            app.Use(async (context, next) =>
            {
                await next();
                if (context.Request.Method == "GET" && context.Response.StatusCode == 200 && !context.Response.Headers.ContainsKey("Cache-Control"))
                {
                    context.Response.Headers["Cache-Control"] = "public, max-age=0, must-revalidate";
                }
            });

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


            app.MapControllers();

            app.Run();
        }
    }
}

