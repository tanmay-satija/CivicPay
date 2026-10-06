using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CivicPay.Application;
using Microsoft.AspNetCore.Hosting;
namespace CivicPay.IntegrationTests;

public class SecureTestApp : TestApp
{
    internal const string TestKey = "synthetic-test-key-not-a-secret-00000000";
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        base.ConfigureWebHost(b);
        b.UseSetting("Demo:OpenAccess", "false");
        b.UseSetting("Security:ApiKey", TestKey);
    }
}
public class SecurityTests
{
    [Fact]
    public async Task Secured_mode_requires_key_and_documents_it_in_OpenApi()
    {
        using var app = new SecureTestApp();
        using var client = app.CreateClient();
        var unauthorized = await client.GetAsync("/api/municipalities");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await unauthorized.Content.ReadFromJsonAsync<ApiError>())!.ErrorCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", SecureTestApp.TestKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/municipalities")).StatusCode);
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.Equal("X-Api-Key", swagger.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("name").GetString());
        Assert.True(swagger.GetProperty("security")[0].TryGetProperty("ApiKey", out _));
    }
}
