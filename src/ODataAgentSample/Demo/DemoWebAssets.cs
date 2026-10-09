namespace ODataAgentSample.Demo;

/// <summary>
/// Liefert Landingpage, Demo-Seite und die zugehoerigen Dateien aus den eingebetteten Ressourcen.
/// Es werden nur Dateien aus dieser Liste ausgeliefert (kein Dateisystemzugriff, kein Pfad aus der Anfrage).
/// Die Content-Security-Policy erlaubt nur eigene Skripte und Stile: kein Inline-Code, keine fremden Server.
/// </summary>
public static class DemoWebAssets
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    private static readonly Dictionary<string, byte[]> Files = Load();

    public static void Map(WebApplication app)
    {
        MapPage(app, "/", "index.html");
        MapPage(app, "/demo", "demo.html");

        app.MapGet("/assets/{file}", (HttpContext context, string file) =>
            Serve(context, "assets/" + file, "no-cache")).AllowAnonymous();
    }

    private static void MapPage(WebApplication app, string route, string file) =>
        app.MapGet(route, (HttpContext context) => Serve(context, file, "no-cache")).AllowAnonymous();

    private static IResult Serve(HttpContext context, string name, string cacheControl)
    {
        if (!Files.TryGetValue(name, out var content))
            return Results.NotFound();

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers.CacheControl = cacheControl;

        return Results.Bytes(content, ContentTypeOf(name));
    }

    private static string ContentTypeOf(string name) => Path.GetExtension(name) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static Dictionary<string, byte[]> Load()
    {
        const string prefix = "Web/";
        var assembly = typeof(DemoWebAssets).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            // Der Pfad kann je nach Betriebssystem beim Bauen mit \ oder / entstehen.
            files[resource[prefix.Length..].Replace('\\', '/')] = memory.ToArray();
        }

        return files;
    }
}
