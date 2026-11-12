namespace Studeo.Api.Contracts;

/// <summary>New account. Role is "student" or "teacher" and is fixed from then on (only an admin changes it in Keycloak).</summary>
public record RegisterRequest(string Email, string Password, string FirstName, string LastName, string Role);

public record LoginRequest(string Email, string Password);

/// <summary>Password rules shared with the UI (Keycloak's realm policy is length(8) too).</summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
}
