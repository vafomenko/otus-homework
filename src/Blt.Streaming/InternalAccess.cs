using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Blt.Streaming;

public sealed class InternalAccess(IConfiguration config) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = config["Internal:ApiKey"];
        var actual = context.HttpContext.Request.Headers["X-Service-Key"].ToString();
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
            return Results.Unauthorized();
        return await next(context);
    }
}
