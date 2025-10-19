using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.AspNetCore.SignalR;

public sealed class ActiveVisitorsHub : Hub
{
    private readonly IActiveVisitorRepository _activeVisitorRepo;
    private readonly ILogger<ActiveVisitorsHub> _logger;
    private readonly IGeoIPService _geoIpService;

    public ActiveVisitorsHub(
        IActiveVisitorRepository activeVisitorRepo,
        ILogger<ActiveVisitorsHub> logger,
        IGeoIPService geoIpService)
    {
        _activeVisitorRepo = activeVisitorRepo;
        _logger = logger;
        _geoIpService = geoIpService;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var connectionId = Context.ConnectionId;
        _logger.LogInformation($"[HUB OnConnected] ConnectionId: {connectionId}");
        await base.OnConnectedAsync();
    }

    public async Task GetVisitorCount()
    {
        var httpContext = Context.GetHttpContext();
        var connectionId = Context.ConnectionId;

        // Read from cookie
        var visitorIdCookie = httpContext.Request.Cookies["VisitorId"];
        var visitorIdAsGuid = Guid.Parse(visitorIdCookie);

        var currentIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var location = _geoIpService.GetLocationByIp(currentIp);

        var visitor = new ActiveVisitorDto(
            SessionId: Guid.Parse(visitorIdCookie), // now it's safely a Guid
            Ip: currentIp,
            CurrentPath: httpContext.Request.Headers["Referer"].ToString() ?? "unknown",
            LastSeenUtc: DateTime.UtcNow,
            Country: location?.Country ?? "unknown",
            City: location?.City ?? "unknown",
            UserAgent: httpContext.Request.Headers["User-Agent"].ToString()
        );

        // Pass the GUID to your repo
        await _activeVisitorRepo.CreateActiveVisitor(visitor);

        var count = await _activeVisitorRepo.GetActiveVisitorCount();
        await Clients.All.SendAsync("ReceiveVisitorCount", count);
    }

}