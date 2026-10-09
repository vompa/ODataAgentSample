using System.Net;
using System.Text;
using System.Text.Json;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.OData;

/// <summary>Jeder Test startet eine eigene Anwendung mit frisch gesaeter Datenbank und ist damit von allen anderen unabhaengig.</summary>
public sealed class ODataErrorHandlingTests
{
    private const int UnknownId = 999_999;

    private static readonly Uri Countries = new("/odata/v1/Countries", UriKind.Relative);
    private static readonly Uri UnknownCountry = new($"/odata/v1/Countries({UnknownId})", UriKind.Relative);
    private static readonly Uri KnownCountry = new("/odata/v1/Countries(40)", UriKind.Relative);

    private sealed record CountryDto(int Id, string? ISO2, string DisplayNameGER, string? EU);

    private sealed record CountryPage(List<CountryDto> Value);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<List<CountryDto>> ReadAllCountriesAsync(HttpClient client)
    {
        var all = new List<CountryDto>();
        for (var skip = 0; ; skip += 100)
        {
            var response = await client.GetAsync(new Uri($"/odata/v1/Countries?$orderby=Id&$skip={skip}&$top=100", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = JsonSerializer.Deserialize<CountryPage>(await response.Content.ReadAsStringAsync(), JsonOptions);
            var items = Assert.IsType<CountryPage>(page).Value;
            all.AddRange(items);
            if (items.Count < 100)
                return all;
        }
    }

    [Fact]
    public async Task Get_unknown_id_returns_404()
    {
        using var factory = new AppFactory();

        var response = await factory.CreateWriterClient().GetAsync(UnknownCountry);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_unknown_id_returns_404_and_leaves_data_unchanged()
    {
        using var factory = new AppFactory();
        var client = factory.CreateWriterClient();
        var before = await ReadAllCountriesAsync(client);

        var response = await client.PatchAsync(UnknownCountry, Json("""{"DisplayNameGER":"Phantom"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await ReadAllCountriesAsync(client));
    }

    [Fact]
    public async Task Delete_unknown_id_returns_404_and_leaves_data_unchanged()
    {
        using var factory = new AppFactory();
        var client = factory.CreateWriterClient();
        var before = await ReadAllCountriesAsync(client);

        var response = await client.DeleteAsync(UnknownCountry);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(264, before.Count);
        Assert.Equal(before, await ReadAllCountriesAsync(client));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("[1,2,")]
    public async Task Patch_with_malformed_json_returns_400_and_leaves_data_unchanged(string body)
    {
        using var factory = new AppFactory();
        var client = factory.CreateWriterClient();
        var before = await ReadAllCountriesAsync(client);

        var response = await client.PatchAsync(KnownCountry, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadAllCountriesAsync(client));
    }

    [Fact]
    public async Task Delete_known_id_removes_exactly_that_record()
    {
        using var factory = new AppFactory();
        var client = factory.CreateWriterClient();
        var before = await ReadAllCountriesAsync(client);

        var response = await client.DeleteAsync(KnownCountry);
        var after = await ReadAllCountriesAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before.Where(c => c.Id != 40), after);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(KnownCountry)).StatusCode);
    }
}
