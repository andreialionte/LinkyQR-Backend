using Delta;
using Linky.DataLayer;
using Linky.IRepository;
using Linky.IService;
using Linky.Jobs;
using Linky.Mappers;
using Linky.Middlewares.Linky.Middleware;
using Linky.Repository;
using Linky.Service;
using Linky.Utils;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using OwaspHeaders.Core.Extensions;
using Quartz;
using StackExchange.Redis;
using System.Data;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.NewtonsoftJson;

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
                });
            });

            int cpuCores = Environment.ProcessorCount;
            int tickerQConcurrency = Math.Max(1, (int)(cpuCores * 0.25));

            builder.Services.AddSignalR();



            //builder.Services.AddReverseProxy()
            //    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));


            // builder.Services.AddMemoryCache();



            builder.Services.AddFusionCache()
                .WithSerializer(new FusionCacheNewtonsoftJsonSerializer(new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                    NullValueHandling = NullValueHandling.Ignore
                }))
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
                        IsFailSafeEnabled = true,
                        FailSafeMaxDuration = TimeSpan.FromHours(6),
                        FailSafeThrottleDuration = TimeSpan.FromSeconds(2),
                        JitterMaxDuration = TimeSpan.FromSeconds(30),
                        SkipDistributedCacheReadWhenStale = true,
                        AllowBackgroundDistributedCacheOperations = true
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
                .AddCustomHeader("Cross-Origin-Resource-Policy", "same-origin")
                .AddCustomHeader("Cache-Control", "max-age=0, no-store");

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

            // custom middleware stores last ETag per path and replays it on
            // subsequent GETs when the client forgot to send If-None-Match. this
            // makes curl behave like a browser and triggers 304s automatically.
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == "GET")
                {
                    var cache = context.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                    var key = "etag:" + context.Request.Path + context.Request.QueryString;
                    if (!context.Request.Headers.ContainsKey("If-None-Match") && cache.TryGetValue(key, out string? previous))
                    {
                        context.Request.Headers["If-None-Match"] = previous;
                    }

                    // run rest of pipeline first so Delta computes a new ETag
                    await next();

                    if (context.Response.Headers.TryGetValue("ETag", out var current))
                    {
                        cache.Set(key, current.ToString(), TimeSpan.FromMinutes(5));
                    }

                    return;
                }

                await next();
            });

            // Delta Library https://github.com/SimonCropp/Delta/blob/main/docs/postgres.md
            app.UseDelta<DataContextEf>();

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

            app.Use(async (context, next) =>
            {
                await next();
                if (context.Request.Method == "GET" && context.Response.StatusCode == 200 && !context.Response.Headers.ContainsKey("Cache-Control"))
                {
                    context.Response.Headers["Cache-Control"] = "public, max-age=3600";
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

