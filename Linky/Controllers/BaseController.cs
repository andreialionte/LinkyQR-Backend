using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Linky.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("spam-api")]
    public class BaseController : ControllerBase
    {
        //protected IActionResult ApiResponse<T>(bool success, T? data = default, string? message = null)
        //{
        //    var result = new
        //    {
        //        success,
        //        message,
        //        data
        //    };
        //    return Ok(result);
        //}

        //protected void AddCacheHeaders(int seconds)
        //{
        //    Response.Headers["Cache-Control"] = $"public,max-age={seconds}";
        //}
    }
}
