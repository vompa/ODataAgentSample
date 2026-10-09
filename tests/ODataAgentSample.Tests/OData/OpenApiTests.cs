using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.OData;

/// <summary>
/// Die Swagger-Oberflaeche ist standardmaessig aus. Eingeschaltet sind Oberflaeche und OpenAPI-Dokument anonym erreichbar
/// (sie beschreiben nur die Schnittstelle), die Daten selbst bleiben hinter dem API-Key.
/// </summary>
public sealed class OpenApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private WebApplicationFactory<Program> Enabled() =>
        factory.WithWebHostBuilder(b => b.UseSetting("OpenApi:Enabled", "true"));

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/odata.json")]
    public async Task By_default_swagger_and_the_document_are_not_served(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_serves_swagger_ui_without_a_key()
    {
        using var app = Enabled();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enabled_document_describes_the_odata_api_with_an_api_key_scheme()
    {
        using var app = Enabled();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/odata.json", UriKind.Relative));
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("3.", document.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("/odata/v1", document.GetProperty("servers")[0].GetProperty("url").GetString());

        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/Countries", out _));
        Assert.True(paths.TryGetProperty("/WorldRegions", out _));
        Assert.True(paths.TryGetProperty("/CountryRegions", out _));

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        Assert.True(document.GetProperty("security").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Enabled_document_lists_only_operations_the_api_really_supports()
    {
        using var app = Enabled();
        using var client = app.CreateClient();

        var json = await client.GetStringAsync(new Uri("/openapi/odata.json", UriKind.Relative));
        var paths = JsonDocument.Parse(json).RootElement.GetProperty("paths");

        static string[] Methods(JsonElement path) =>
            path.EnumerateObject().Select(p => p.Name).Where(n => n is "get" or "post" or "put" or "patch" or "delete").Order().ToArray();

        Assert.Equal(["get", "post"], Methods(paths.GetProperty("/Countries")));
        Assert.Equal(["delete", "get", "patch"], Methods(paths.GetProperty("/Countries({Id})")));
        Assert.Equal(["get"], Methods(paths.GetProperty("/WorldRegions")));
        Assert.Equal(["get"], Methods(paths.GetProperty("/WorldRegions({Id})")));
        Assert.Equal(["get"], Methods(paths.GetProperty("/CountryRegions")));
        Assert.Equal(["get"], Methods(paths.GetProperty("/CountryRegions({Id})")));

        // Kein Pfad verspricht irgendwo sonst eine veraendernde Operation
        foreach (var path in paths.EnumerateObject().Where(p => p.Name is not ("/Countries" or "/Countries({Id})")))
        {
            Assert.Equal(["get"], Methods(path.Value));
        }
    }

    [Fact]
    public async Task Enabled_document_does_not_depend_on_the_host_header()
    {
        using var app = Enabled();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/openapi/odata.json", UriKind.Relative));
        request.Headers.Host = "evil.example";

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("evil.example", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/odata/v1/Countries?$top=1")]
    [InlineData("/odata/v1/$metadata")]
    [InlineData("/mcp")]
    public async Task Enabled_still_protects_the_data_and_mcp_endpoints(string path)
    {
        using var app = Enabled();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
