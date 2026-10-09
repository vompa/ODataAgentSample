using System.Net;
using System.Text;
using System.Text.Json;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.OData;

public sealed class RegionEndpointTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private sealed record RegionDto(int Id, string ISO, string Name, string NameGER);

    private sealed record RegionPage(List<RegionDto> Value);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private async Task<List<RegionDto>> ReadRegionsAsync(string url)
    {
        var response = await factory.CreateReaderClient().GetAsync(new Uri(url, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = JsonSerializer.Deserialize<RegionPage>(await response.Content.ReadAsStringAsync(), JsonOptions);
        return Assert.IsType<RegionPage>(page).Value;
    }

    [Theory]
    [InlineData("/odata/v1/WorldRegions", 6, 6)]
    [InlineData("/odata/v1/CountryRegions", 10, 100)]
    public async Task Reader_gets_region_collections_with_plausible_size(string url, int min, int max)
    {
        var regions = await ReadRegionsAsync(url);

        Assert.InRange(regions.Count, min, max);
        Assert.All(regions, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.ISO));
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
        });
        Assert.Equal(regions.Count, regions.Select(r => r.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("/odata/v1/WorldRegions", "POST")]
    [InlineData("/odata/v1/WorldRegions", "PATCH")]
    [InlineData("/odata/v1/WorldRegions", "DELETE")]
    [InlineData("/odata/v1/CountryRegions", "POST")]
    [InlineData("/odata/v1/CountryRegions", "PATCH")]
    [InlineData("/odata/v1/CountryRegions", "DELETE")]
    public async Task Region_collections_offer_no_write_operations_and_stay_unchanged(string url, string method)
    {
        var before = await ReadRegionsAsync(url);
        var target = new Uri(method == "POST" ? url : $"{url}({before[0].Id})", UriKind.Relative);
        HttpRequestMessage NewRequest()
        {
            var request = new HttpRequestMessage(new HttpMethod(method), target);
            if (method != "DELETE")
                request.Content = Json("""{"Name":"Hacked","NameGER":"Hacked","ISO":"ZZ"}""");
            return request;
        }

        var readerResponse = await factory.CreateReaderClient().SendAsync(NewRequest());
        var writerResponse = await factory.CreateWriterClient().SendAsync(NewRequest());

        // Es gibt keine Schreib-Aktion im Controller: Die Route existiert nicht, daher 404/405 fuer jede Rolle.
        Assert.Contains(readerResponse.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains(writerResponse.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(before, await ReadRegionsAsync(url));
    }
}
