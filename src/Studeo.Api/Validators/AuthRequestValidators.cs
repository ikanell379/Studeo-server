using FluentValidation;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        // One Must() per field, so the error code covers every way the field can be wrong.
        RuleFor(r => r.Email)
            .Must(BeAnEmail)
            .WithErrorCode(ErrorCodes.InvalidEmail).WithMessage("A valid email is required.");

        RuleFor(r => r.Password)
            .Must(p => p is { Length: >= PasswordRules.MinLength and <= PasswordRules.MaxLength })
            .WithErrorCode(ErrorCodes.WeakPassword)
            .WithMessage($"The password needs {PasswordRules.MinLength}–{PasswordRules.MaxLength} characters.");

        RuleFor(r => r.FirstName)
            .Must(BeAName)
            .WithErrorCode(ErrorCodes.InvalidRequest).WithMessage("First name is required (max 100 characters).");

        RuleFor(r => r.LastName)
            .Must(BeAName)
            .WithErrorCode(ErrorCodes.InvalidRequest).WithMessage("Last name is required (max 100 characters).");

        RuleFor(r => r.Role)
            .Must(role => Roles.All.Contains(role))
            .WithErrorCode(ErrorCodes.InvalidRequest).WithMessage("Role must be 'student' or 'teacher'.");
    }

    private static bool BeAName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;

    private static bool BeAnEmail(string? email)
    {
        email = email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || email.Any(char.IsWhiteSpace)) return false;
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith('.');
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).Must(e => !string.IsNullOrWhiteSpace(e) && e.Length <= 254)
            .WithErrorCode(ErrorCodes.InvalidRequest).WithMessage("Email is required.");
        RuleFor(r => r.Password).Must(p => !string.IsNullOrEmpty(p) && p.Length <= PasswordRules.MaxLength)
            .WithErrorCode(ErrorCodes.InvalidRequest).WithMessage("Password is required.");
    }
}
