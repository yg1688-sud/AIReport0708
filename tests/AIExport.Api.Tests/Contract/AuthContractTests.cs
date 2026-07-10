using System.Net.Http.Json;
using System.Text.Json;

namespace AIExport.Api.Tests.Contract;

public class AuthContractTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthContractTests(CustomWebApplicationFactory factory)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-at-least-32-characters-long!");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "admin123");
        _factory = factory;
    }

    [Fact]
    public async Task POST_Login_ValidCredentials_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "admin123"
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(body.GetProperty("token").GetString());
        Assert.Equal("admin", body.GetProperty("user").GetProperty("username").GetString());
    }

    [Fact]
    public async Task POST_Login_InvalidPassword_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "wrongpassword"
        });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task POST_Login_EmptyUsername_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "",
            password = "test123"
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
