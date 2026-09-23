using System.Text.Json.Serialization;

namespace Blt.Users.Api.Models;

public sealed record RegisterRequest(
    string? Username, string? Password, string? FirstName,
    string? LastName, string? Email, string? Phone)
{
    [JsonIgnore]
    public CreateUserRequest Profile => new(Username, FirstName, LastName, Email, Phone);
}

public sealed record LoginRequest(string? Username, string? Password);

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, long UserId);

public static class AuthValidation
{
    public static string? ValidateRegistration(RegisterRequest? request)
    {
        if (request is null) return "Request body is required.";
        if (request.Password is null || request.Password.Length is < 12 or > 128 ||
            string.IsNullOrWhiteSpace(request.Password))
            return "password must contain between 12 and 128 characters.";
        return UserValidation.ValidateCreate(request.Profile);
    }

    public static bool IsValidLogin(LoginRequest? request) =>
        request is not null && !string.IsNullOrWhiteSpace(request.Username) &&
        request.Username.Trim().Length <= 256 && request.Password is { Length: > 0 and <= 128 };
}
