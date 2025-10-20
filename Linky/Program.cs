using Linky.DataLayer;
using Linky.IRepository;
using Linky.IService;
using Linky.Jobs;
using Linky.Middlewares.Linky.Middleware;
using Linky.Repository;
using Linky.Service;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Quartz;
using StackExchange.Redis;
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

            builder.Services.AddHttpContextAccessor(); //for ips etc i thhink

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
                        .StartNow() //run when app starts
                        .WithSimpleSchedule(x => x
                            .WithIntervalInSeconds(intervalSeconds)
                            .WithMisfireHandlingInstructionFireNow()
                            .RepeatForever()));

                var jobKey2 = new JobKey("AggregateVisitorStatsJob");
                q.AddJob<AggregateVisitorStats>(opts => opts.WithIdentity(jobKey2));

                // Trigger: run daily at 00:05 UTC
                q.AddTrigger(opts => opts
                    .ForJob(jobKey2)
                    .WithIdentity("AggregateVisitorStatsTrigger")
                    .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(0, 5))
                    );
            });

            //// Adaugă Quartz hosted service
            builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.ListenAnyIP(5000, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                    //listenOptions.UseHttps();
                });
            });

            int cpuCores = Environment.ProcessorCount;
            int tickerQConcurrency = Math.Max(1, (int)(cpuCores * 0.25));

            builder.Services.AddSignalR();



            //builder.Services.AddReverseProxy()
            //    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));


            builder.Services.AddMemoryCache();



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
                                .AllowCredentials(); // important for SignalR
                });
            });

            var app = builder.Build();

            // Delta Library https://github.com/SimonCropp/Delta/blob/main/docs/postgres.md
            //app.UseDelta();


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



            app.UseCors("main");
            //app.MapReverseProxy();

            app.UseMiddleware<VisitorIdMiddleware>();
            app.MapHub<ActiveVisitorsHub>("/ActiveVisitorsHub");


            app.UseSecurityHeaders(); // https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders

            //app.UseRateLimiter();


            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();

                app.UseHsts();
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}

