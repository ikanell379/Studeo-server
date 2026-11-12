using System.Security.Claims;

namespace Studeo.Api.Auth;

/// <summary>The Keycloak realm roles of Studeo. A user picks one at registration.</summary>
public static class Roles
{
    public const string Teacher = "teacher";
    public const string Student = "student";

    /// <summary>For [Authorize(Roles = ...)]: any Studeo user.</summary>
    public const string Any = Teacher + "," + Student;

    public static readonly IReadOnlyList<string> All = [Teacher, Student];

    /// <summary>The user's Studeo role, or null if Keycloak hasn't given one yet. Teacher wins if both are set.</summary>
    public static string? Of(ClaimsPrincipal user) =>
        user.IsInRole(Teacher) ? Teacher
        : user.IsInRole(Student) ? Student
        : null;
}
