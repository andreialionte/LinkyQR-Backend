// Converted to Minimal API Endpoints - see Linky/Endpoints/DefaultEndpoints.cs
// and app.MapDefaultEndpoints() in Program.cs. Kept here (commented out) for reference/rollback.
/*
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Linky.Controllers
{
    [ApiController]
    [EnableRateLimiting("public-high-volume-api")]
    public class DefaultController : BaseController
    {
        //redirect if someone its typing "https://linkyqr.com" instead of "https://www.linkyqr.com"
        [HttpGet("/")]
        public IActionResult RedirectToWww()
        {
            return RedirectPermanent("https://www.linkyqr.com");
        }
    }
}
*/
