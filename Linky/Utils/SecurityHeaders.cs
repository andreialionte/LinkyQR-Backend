using OwaspHeaders.Core.Models;

namespace Linky.Utils
{
    public static class SecurityHeaders
    {
        public static SecureHeadersMiddlewareConfiguration CustomConfiguration()
        {
            return SecureHeadersMiddlewareBuilder
                .CreateBuilder()
                .UseHsts(1200, false)
                .UseContentDefaultSecurityPolicy()
                .UsePermittedCrossDomainPolicies(XPermittedCrossDomainOptionValue.masterOnly)
                .UseReferrerPolicy(ReferrerPolicyOptions.sameOrigin)
                .UsePermissionsPolicy()
                .Build();
        }
    }
}