using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Blt.Users.Api.Security;

public static class ProfileOwner
{
    public const string PolicyName = "ProfileOwner";

    public static long UserId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    public static bool IsOwner(AuthorizationHandlerContext context) =>
        context.Resource is HttpContext http &&
        long.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id) &&
        long.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && id == userId;
}
