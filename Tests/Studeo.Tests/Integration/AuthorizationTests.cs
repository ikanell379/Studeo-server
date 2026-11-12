using System.Net;
using Studeo.Api.Auth;

namespace Studeo.Tests.Integration;

/// <summary>Which users may call which endpoints.</summary>
public class AuthorizationTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    public static TheoryData<string> ProtectedUrls =>
    [
        $"/api/documents/{Guid.NewGuid()}",
        $"/api/documents/{Guid.NewGuid()}/sections",
        $"/api/study-sets/{Guid.NewGuid()}",
    ];

    [Theory]
    [MemberData(nameof(ProtectedUrls))]
    public async Task Anonymous_gets_401(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_upload()
    {
        var response = await factory.CreateClient().PostAsync("/api/documents", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedUrls))]
    public async Task Signed_in_user_without_role_gets_403(string url)
    {
        var response = await factory.CreateClientAs(TestUser.WithoutRole()).GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedUrls))]
    public async Task Teacher_and_student_get_through(string url)
    {
        foreach (var user in new[] { TestUser.Teacher, TestUser.Student })
        {
            var response = await factory.CreateClientAs(user).GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // authorized; the random id just doesn't exist
        }
    }

    [Fact]
    public async Task Health_stays_public()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
