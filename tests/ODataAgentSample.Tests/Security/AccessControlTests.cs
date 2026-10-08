using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.Security;

public sealed class AccessControlTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Uri Countries = new("/odata/v1/Countries", UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Health_is_public()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/odata/v1/Countries")]
    [InlineData("/odata/v1/$metadata")]
    [InlineData("/odata/v1/WorldRegions")]
    public async Task OData_requires_authentication(string url)
    {
        var response = await factory.CreateClient().GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_key_is_rejected()
    {
        var response = await factory.CreateClientWithKey("wrong").GetAsync(Countries);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_token_works_like_api_key_header()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppFactory.ReaderKey);

        var response = await client.GetAsync(Countries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reader_can_read_but_not_modify()
    {
        var client = factory.CreateReaderClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Countries)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync(Countries, Json("""{"Name":"X","NameGER":"X","DisplayName":"X","DisplayNameGER":"X"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PatchAsync(new Uri("/odata/v1/Countries(1)", UriKind.Relative), Json("""{"EU":"N"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync(new Uri("/odata/v1/Countries(1)", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Writer_can_patch_a_country()
    {
        var client = factory.CreateWriterClient();

        var patch = await client.PatchAsync(
            new Uri("/odata/v1/Countries(40)", UriKind.Relative), Json("""{"DisplayNameGER":"Geaendert"}"""));
        var read = await client.GetStringAsync(new Uri("/odata/v1/Countries(40)?$select=DisplayNameGER", UriKind.Relative));

        Assert.True(patch.IsSuccessStatusCode, patch.StatusCode.ToString());
        Assert.Contains("Geaendert", read, StringComparison.Ordinal);
    }
}
