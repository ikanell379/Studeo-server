namespace Studeo.Api.Contracts;

public record SectionResponse(Guid Id, int Order, string Title, int StartPage, int EndPage, int WordCount);
