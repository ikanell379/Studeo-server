using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;

namespace Studeo.Tests.Integration;

public class MeControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    [Fact]
    public async Task Anonymous_gets_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Teacher)]
    public async Task Returns_the_user_and_role(string role)
    {
        var user = new TestUser(Guid.NewGuid().ToString(), [role, "offline_access"]);

        var me = await factory.CreateClientAs(user).GetFromJsonAsync<MeResponse>("/api/me");

        Assert.Equal(new MeResponse(user.Id, "Μαρία Παπαδοπούλου", user.Email, role), me);
    }

    [Fact]
    public async Task Teacher_wins_when_user_has_both_roles()
    {
        var user = new TestUser(Guid.NewGuid().ToString(), [Roles.Student, Roles.Teacher]);

        var me = await factory.CreateClientAs(user).GetFromJsonAsync<MeResponse>("/api/me");

        Assert.Equal(Roles.Teacher, me!.Role);
    }

    [Fact]
    public async Task User_without_role_gets_403_no_role()
    {
        var response = await factory.CreateClientAs(TestUser.WithoutRole()).GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.NoRole, json.RootElement.GetProperty("code").GetString());
    }
}
