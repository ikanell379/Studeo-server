namespace Studeo.Api.Contracts;

/// <summary>The signed-in user. Role is "teacher" or "student".</summary>
public record MeResponse(string Id, string Name, string? Email, string Role);
