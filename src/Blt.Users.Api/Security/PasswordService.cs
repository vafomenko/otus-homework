using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Blt.Users.Api.Security;

public sealed class PasswordService
{
    private readonly PasswordHasher<string> _hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = 220_000
    }));
    private readonly string _dummyHash;

    public PasswordService() => _dummyHash = Hash(Guid.NewGuid().ToString("N"));

    public string Hash(string password) => _hasher.HashPassword("", password);

    public bool Verify(string? hash, string password)
    {
        // Unknown logins still perform the expensive password verification.
        var result = _hasher.VerifyHashedPassword("", hash ?? _dummyHash, password);
        return hash is not null && result != PasswordVerificationResult.Failed;
    }
}
