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
            var sessionId = Request.Cookies["VisitorId"];
            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest("Missing VisitorId cookie.");

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var existing = await _visitorRepo.GetVisitorBySessionId(Guid.Parse(sessionId));
            if (existing != null)
            {
                return StatusCode(StatusCodes.Status304NotModified, new { message = "Already created" });
            }

            var geoLoc = _geoIpService.GetLocationByIp(ip);

            var visitorWithSession = visitorDto with
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.Parse(sessionId),            // string stays string in your DTO
                Ip = ip,
                Timestamp = DateTime.UtcNow,
                Country = geoLoc?.Country,
                City = geoLoc?.City,
                IsUnique = true
            };

            await _visitorRepo.AddVisitor(visitorWithSession);

            return CreatedAtAction(nameof(GetVisitorBySessionId),
                new { sessionId = visitorWithSession.SessionId }, visitorWithSession);
        }



        [HttpGet("visitor/{sessionId}")]
        public async Task<IActionResult> GetVisitorBySessionId(string sessionId)
        {
            var visitor = await _visitorRepo.GetVisitorBySessionId(Guid.Parse(sessionId));
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
