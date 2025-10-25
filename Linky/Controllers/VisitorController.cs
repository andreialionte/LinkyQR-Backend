using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    public class VisitorController : BaseController
    {
        private readonly IVisitorRepository _visitorRepo;
        private readonly IGeoIPService _geoIpService;

        public VisitorController(IVisitorRepository visitorRepo, IGeoIPService geoIpSerive)
        {
            _visitorRepo = visitorRepo;
            _geoIpService = geoIpSerive;
        }

        [HttpPost("AddVisitor")]
        public async Task<IActionResult> AddVisitor([FromBody] VisitorDto visitorDto)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var geoLoc = _geoIpService.GetLocationByIp(ip);

            var visitorWithSession = visitorDto with
            {
                Id = Guid.NewGuid(),
                Ip = ip,
                Timestamp = DateTime.UtcNow,
                Country = geoLoc?.Country,
                City = geoLoc?.City,
                IsUnique = true
            };

            await _visitorRepo.AddVisitor(visitorWithSession);

            return Ok(new { message = "Visitor added successfully", id = visitorWithSession.Id });
        }

        [HttpGet("visitor/{visitorId}")]
        public async Task<IActionResult> GetVisitorById(Guid visitorId)
        {
            var visitor = await _visitorRepo.GetVisitorBySessionId(visitorId);
            if (visitor == null) return NotFound();
            return Ok(visitor);
        }

        [HttpGet("RecentVisitors")]
        public async Task<IActionResult> GetRecentVisitors([FromQuery] int limit = 100)
        {
            var visitors = await _visitorRepo.GetRecentVisitors(limit);
            if (visitors == null || !visitors.Any())
                return NotFound("No visitors found.");
            return Ok(visitors);
        }
    }
}