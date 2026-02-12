using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Linky.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    public class VisitorController : BaseController
    {
        private readonly IVisitorRepository _visitorRepo;
        private readonly IGeoIPService _geoIpService;
        private readonly IClientIp _clientIp;
        private readonly VisitorMapper _mapper;

        public VisitorController(IVisitorRepository visitorRepo, IGeoIPService geoIpSerive, IClientIp clientIp, VisitorMapper mapper)
        {
            _visitorRepo = visitorRepo;
            _mapper = mapper;
            _geoIpService = geoIpSerive;
            _clientIp = clientIp;
        }

        [HttpPost("AddVisitor")]
        public async Task<IActionResult> AddVisitor([FromBody] VisitorDto visitorDto)
        {
            Guid sessionId = Guid.Empty;
            if (Request.Cookies.TryGetValue("VisitorId", out var cookieValue) && Guid.TryParse(cookieValue, out var parsed))
            {
                sessionId = parsed;
            }

            var ip = _clientIp.GetClientIp();
            var geoLoc = _geoIpService.GetLocationByIp(ip);

            var visitorWithSession = visitorDto with
            {
                Id = sessionId == Guid.Empty ? Guid.NewGuid() : sessionId,
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
            var dto = _mapper.ToDto(visitor);
            return Ok(dto);
        }

        [HttpGet("RecentVisitors")]
        public async Task<IActionResult> GetRecentVisitors([FromQuery] int limit = 100)
        {
            var visitors = await _visitorRepo.GetRecentVisitors(limit);
            if (visitors == null || !visitors.Any())
                return NotFound("No visitors found.");
            var dtos = visitors.Select(_mapper.ToDto);
            return Ok(dtos);
        }
    }
}