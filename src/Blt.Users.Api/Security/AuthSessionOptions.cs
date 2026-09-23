namespace Blt.Users.Api.Security;

public sealed class AuthSessionOptions
{
    public int LifetimeMinutes { get; set; } = 60;
}
