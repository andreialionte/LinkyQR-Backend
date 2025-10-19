//using Linky.Hubs;
//using Linky.IRepository;
//using Linky.Models;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;

//namespace Linky.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class ActiveVisitorController : ControllerBase
//    {
//        private readonly IActiveVisitorRepository _activeVisitorRepo;
//        private readonly IHubContext<ActiveVisitorsHub> _hubContext;

//        public ActiveVisitorController(
//            IActiveVisitorRepository activeVisitorRepo,
//            IHubContext<ActiveVisitorsHub> hubContext)
//        {
//            _activeVisitorRepo = activeVisitorRepo;
//            _hubContext = hubContext;
//        }

//        // Păstrezi endpoint-ul dacă ai nevoie de lista completă
//        [HttpGet("GetActiveVisitors")]
//        public async Task<ActionResult<IList<ActiveVisitor>>> GetActiveVisitors()
//        {
//            return Ok(await _activeVisitorRepo.GetActiveVisitors());
//        }

//        // Sau doar count
//        [HttpGet("GetActiveVisitorsCount")]
//        public async Task<ActionResult<int>> GetActiveVisitorsCount()
//        {
//            var visitors = await _activeVisitorRepo.GetActiveVisitors();
//            return Ok(visitors.Count);
//        }

//        // Când faci update în DB (ex: adaugi/ștergi visitor), notifici clienții SignalR
//        [HttpPost("NotifyVisitorsChanged")]
//        public async Task<IActionResult> NotifyVisitorsChanged()
//        {
//            var visitors = await _activeVisitorRepo.GetActiveVisitors();
//            await _hubContext.Clients.All.SendAsync("ReceiveVisitorCount", visitors.Count);
//            return Ok();
//        }
//    }
//}