using System.Net;
using System.Text.Json;
using ODataAgentSample.Security;
using ODataAgentSample.Tests.Support;

namespace ODataAgentSample.Tests.OData;

/// <summary>
/// Hinter einem TLS-Proxy kommt das Schema nur als X-Forwarded-Proto an. Die Auswertung ist abschaltbar, weil sonst jeder,
/// der die App direkt erreicht, Schema und Client-IP (und damit das IP-basierte Rate Limit) faelschen koennte.
/// </summary>
public sealed class ForwardedHeadersTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static async Task<string> ODataContextAsync(HttpClient client, string forwardedProto)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/odata/v1/Countries?$top=1", UriKind.Relative));
        request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("@odata.context").GetString()!;
    }

    [Fact]
    public async Task With_forwarded_headers_enabled_odata_links_use_the_scheme_of_the_proxy()
    {
        using var enabled = factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:Enabled", "true"));

        using var client = enabled.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, AppFactory.ReaderKey);

        var context = await ODataContextAsync(client, "https");

        Assert.StartsWith("https://", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_setting_forwarded_headers_are_ignored()
    {
        var context = await ODataContextAsync(factory.CreateReaderClient(), "https");

        Assert.StartsWith("http://", context, StringComparison.Ordinal);
    }
}
