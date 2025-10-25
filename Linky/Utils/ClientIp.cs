namespace Linky.Utils
{
    public class ClientIp : IClientIp
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ClientIp(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetClientIp()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null)
                return "unknown";

            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
            {
                var ip = forwarded.ToString().Split(',').FirstOrDefault();
                if (!string.IsNullOrEmpty(ip))
                    return ip.Trim();
            }

            if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
            {
                if (!string.IsNullOrEmpty(realIp.ToString()))
                    return realIp.ToString();
            }

            return context.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}