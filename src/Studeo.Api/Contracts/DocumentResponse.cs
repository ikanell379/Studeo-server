namespace Studeo.Api.Contracts;

public record DocumentResponse(Guid Id, string FileName, int PageCount, DateTimeOffset UploadedAt);
