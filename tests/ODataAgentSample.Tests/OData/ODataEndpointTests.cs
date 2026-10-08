using System.Net;
using System.Text.Json;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.OData;

public sealed class ODataEndpointTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await factory.CreateReaderClient().GetAsync(new Uri(url, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Count_matches_seed_data()
    {
        var response = await factory.CreateReaderClient().GetStringAsync(new Uri("/odata/v1/Countries/$count", UriKind.Relative));

        Assert.Equal("264", response);
    }

    [Fact]
    public async Task Filter_and_select_work()
    {
        var json = await GetJsonAsync("/odata/v1/Countries?$filter=ISO2 in ('AT','DE')&$select=ISO2,ISO3&$orderby=ISO3");

        var values = json.GetProperty("value");
        Assert.Equal(2, values.GetArrayLength());
        Assert.Equal("AUT", values[0].GetProperty("ISO3").GetString());
        Assert.Equal("DEU", values[1].GetProperty("ISO3").GetString());
    }

    [Fact]
    public async Task Expand_resolves_world_region()
    {
        var json = await GetJsonAsync("/odata/v1/Countries?$filter=ISO2 eq 'AT'&$expand=WorldRegion,CountryRegion");

        var austria = json.GetProperty("value")[0];
        Assert.Equal("Europe", austria.GetProperty("WorldRegion").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Custom_search_filters_by_world_region()
    {
        var json = await GetJsonAsync("/odata/v1/Countries?$search=LaenderInEuropa&$count=true&$top=1");

        var europe = json.GetProperty("@odata.count").GetInt32();
        Assert.InRange(europe, 40, 70);
    }

    [Fact]
    public async Task Apply_groupby_aggregates_per_region()
    {
        var json = await GetJsonAsync("/odata/v1/Countries?$apply=groupby((WorldRegionId),aggregate($count as CountryCount))");

        Assert.Equal(6, json.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Collection_is_paged_to_protect_the_server()
    {
        var json = await GetJsonAsync("/odata/v1/Countries");

        Assert.Equal(100, json.GetProperty("value").GetArrayLength());
        Assert.True(json.TryGetProperty("@odata.nextLink", out _));
    }

    [Fact]
    public async Task Top_above_limit_is_rejected()
    {
        var response = await factory.CreateReaderClient().GetAsync(new Uri("/odata/v1/Countries?$top=1000", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
