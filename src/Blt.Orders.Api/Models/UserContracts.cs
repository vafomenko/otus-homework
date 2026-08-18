using System.ComponentModel.DataAnnotations;

namespace Blt.Orders.Api.Models;

public sealed record UserResponse(
    long Id,
    string Username,
    string FirstName,
    string LastName,
    string Email,
    string? Phone);

public sealed record CreateUserRequest(
    string? Username,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone);

// The reference contract intentionally allows username to be omitted on PUT.
// In that case the current username is retained.
public sealed record UpdateUserRequest(
    string? Username,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone);

public static class UserValidation
{
    public static string? ValidateCreate(CreateUserRequest? request)
    {
        if (request is null)
        {
            return "Request body is required.";
        }

        return ValidateFields(
            request.Username,
            request.FirstName,
            request.LastName,
            request.Email,
            requireUsername: true,
            requireNames: true);
    }

    public static string? ValidateUpdate(UpdateUserRequest? request)
    {
        if (request is null)
        {
            return "Request body is required.";
        }

        if (request.Username is null &&
            request.FirstName is null &&
            request.LastName is null &&
            request.Email is null &&
            request.Phone is null)
        {
            return "At least one field must be provided.";
        }

        return ValidateFields(
            request.Username,
            request.FirstName,
            request.LastName,
            request.Email,
            requireUsername: false,
            requireNames: false);
    }

    public static CreateUserRequest Normalize(CreateUserRequest request)
    {
        return request with
        {
            Username = request.Username?.Trim(),
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            Email = request.Email?.Trim(),
            Phone = NormalizeOptional(request.Phone)
        };
    }

    public static UpdateUserRequest Normalize(UpdateUserRequest request)
    {
        return request with
        {
            Username = NormalizeOptional(request.Username),
            FirstName = NormalizeOptional(request.FirstName),
            LastName = NormalizeOptional(request.LastName),
            Email = NormalizeOptional(request.Email),
            Phone = NormalizeOptional(request.Phone)
        };
    }

    private static string? ValidateFields(
        string? username,
        string? firstName,
        string? lastName,
        string? email,
        bool requireUsername,
        bool requireNames)
    {
        if (requireUsername && string.IsNullOrWhiteSpace(username) ||
            username is not null && username.Trim().Length is < 1 or > 256)
        {
            return "username must contain between 1 and 256 characters.";
        }

        if (requireNames && string.IsNullOrWhiteSpace(firstName) ||
            firstName is not null && firstName.Trim().Length is < 1 or > 256)
        {
            return "firstName must contain between 1 and 256 characters.";
        }

        if (requireNames && string.IsNullOrWhiteSpace(lastName) ||
            lastName is not null && lastName.Trim().Length is < 1 or > 256)
        {
            return "lastName must contain between 1 and 256 characters.";
        }

        if (email is not null &&
            (email.Trim().Length is < 3 or > 320 ||
             !new EmailAddressAttribute().IsValid(email.Trim())))
        {
            return "email must be a valid email address.";
        }

        if (firstName is not null && firstName.Trim().Length == 0 ||
            lastName is not null && lastName.Trim().Length == 0)
        {
            return "Names cannot be empty.";
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
