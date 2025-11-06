using System.Security.Claims;

namespace SkyFy_Api
{
    public class RequestHelper
    {
        public static string GetUserIDFromClaims(ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
