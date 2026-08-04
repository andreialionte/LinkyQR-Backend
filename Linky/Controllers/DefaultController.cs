using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Linky.Controllers
{
    [ApiController]
    [DisableRateLimiting]
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