using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
