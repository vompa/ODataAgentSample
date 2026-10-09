using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ODataAgentSample.Security;

namespace ODataAgentSample.Demo;

/// <summary>Schaltet die Demo ein. Wird nur aufgerufen, wenn <c>Demo:Enabled</c> gesetzt ist (Standard: aus).</summary>
public static class DemoSetup
{
    public const string DemoClientName = "demo";

    public static IServiceCollection AddDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoOptions>().Bind(configuration.GetSection(DemoOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DemoOptions>, DemoOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new DemoQuota(sp.GetRequiredService<IOptions<DemoOptions>>().Value, sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<DemoLoginThrottle>();
        services.TryAddSingleton<IDemoToolboxFactory, McpToolboxFactory>();
        services.TryAddSingleton<IDemoAgent, ClaudeDemoAgent>();

        // Die Demo ruft ihren eigenen MCP-Endpunkt mit einem Reader-Key auf. Er wird hier registriert, nie als Writer.
        services.AddOptions<ApiKeyOptions>().PostConfigure<IOptions<DemoOptions>>((apiKeys, demo) =>
            apiKeys.ApiKeys.Add(new ApiKeyEntry
            {
                Name = DemoClientName,
                KeySha256 = ApiKeyHasher.Hash(demo.Value.ApiKey),
                Role = Roles.Reader,
            }));

        return services;
    }

    public static void MapDemo(this WebApplication app)
    {
        DemoEndpoints.Map(app);
        DemoWebAssets.Map(app);
    }
}
