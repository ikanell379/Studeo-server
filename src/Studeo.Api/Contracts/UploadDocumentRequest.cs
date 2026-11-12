namespace Studeo.Api.Contracts;

public class UploadDocumentRequest
{
    public IFormFile? File { get; set; }

    /// <summary>Optional: one of the user's courses to put the PDF in.</summary>
    public Guid? CourseId { get; set; }
}
