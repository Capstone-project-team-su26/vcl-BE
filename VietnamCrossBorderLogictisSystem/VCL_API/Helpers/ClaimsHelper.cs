using System.Security.Claims;

namespace VCL_API.Helpers
{
    public static class ClaimsHelper
    {
        public static string? GetCurrentUserId(ClaimsIdentity? identity)
        {
            if (identity != null)
            {
                var userClaims = identity.Claims;
                return userClaims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
            }
            return null;
        }

        public static string? GetCurrentUserRole(ClaimsIdentity? identity)
        {
            if (identity != null)
            {
                var userClaims = identity.Claims;
                return userClaims.FirstOrDefault(x => x.Type == ClaimTypes.Role)?.Value;
            }
            return null;
        }
    }
}
