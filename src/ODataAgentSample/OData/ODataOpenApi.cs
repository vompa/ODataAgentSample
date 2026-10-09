using Microsoft.OData.Edm;
using Microsoft.OpenApi;
using Microsoft.OpenApi.OData;
using ODataAgentSample.Security;

namespace ODataAgentSample.OData;

/// <summary>
/// Erzeugt aus dem EDM-Modell ein OpenAPI-Dokument fuer die Swagger-Oberflaeche. Das Dokument beschreibt nur die
/// Schnittstelle, es enthaelt keine Daten. Alle Aufrufe in der Oberflaeche brauchen weiterhin einen API-Key.
/// </summary>
public static class ODataOpenApi
{
    public const string RoutePrefix = "odata/v1";
    public const string DocumentPath = "/openapi/odata.json";
    public const string SecuritySchemeName = "ApiKey";

    private static readonly Lazy<string> Json = new(() => CreateJson(CountriesEdmModel.Build()));

    /// <summary>Einmal erzeugt und dann wiederverwendet. Es haengt nicht von der Anfrage ab (relative Server-URL).</summary>
    public static string GetJson() => Json.Value;

    internal static string CreateJson(IEdmModel model)
    {
        var settings = new OpenApiConvertSettings
        {
            // Die Adresse ist nur ein Platzhalter fuer den Generator. Unten wird sie durch eine relative URL ersetzt,
            // damit das Dokument hinter jedem Host und Proxy stimmt und nicht vom Host-Header abhaengt.
            ServiceRoot = new Uri($"https://localhost/{RoutePrefix}"),
            EnableOperationId = true,
        };

        var document = model.ConvertToOpenApi(settings);

        RemoveUnsupportedOperations(document);

        document.Info.Title = "ODataAgentSample (OData v4)";
        document.Info.Description =
            "OData-Dienst mit Laenderdaten. Jeder Aufruf braucht einen API-Key im Header X-Api-Key (Schaltflaeche *Authorize*). " +
            "Die Rolle reader darf lesen, writer zusaetzlich aendern. Schreibbar ist nur Countries (POST, PATCH, DELETE); " +
            "WorldRegions und CountryRegions sind schreibgeschuetzt.";

        document.Servers = [new OpenApiServer { Url = "/" + RoutePrefix }];

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyDefaults.HeaderName,
            Description = "API-Key der Rolle reader oder writer.",
        };

        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = [] },
        ];

        using var writer = new StringWriter();
        document.SerializeAsV3(new OpenApiJsonWriter(writer));
        return writer.ToString();
    }

    /// <summary>
    /// Der Generator beschreibt alle Operationen, die das Modell theoretisch erlaubt. Die Controller bieten aber nur lesenden
    /// Zugriff plus POST, PATCH und DELETE auf Countries. Die Beschreibung soll nichts versprechen, was die API nicht haelt.
    /// </summary>
    private static void RemoveUnsupportedOperations(OpenApiDocument document)
    {
        if (document.Paths is null)
        {
            return;
        }

        foreach (var (path, item) in document.Paths.ToList())
        {
            if (item.Operations is null)
            {
                continue;
            }

            foreach (var method in item.Operations.Keys.ToList())
            {
                if (!IsSupported(path, method))
                {
                    item.Operations.Remove(method);
                }
            }

            if (item.Operations.Count == 0)
            {
                document.Paths.Remove(path);
            }
        }
    }

    private static bool IsSupported(string path, HttpMethod method) =>
        method == HttpMethod.Get
        || (path == "/Countries" && method == HttpMethod.Post)
        || (path == "/Countries({Id})" && (method == HttpMethod.Patch || method == HttpMethod.Delete));
}
