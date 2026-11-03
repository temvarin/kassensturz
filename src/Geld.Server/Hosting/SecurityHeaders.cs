namespace Geld.Server.Hosting;

public static class SecurityHeaders
{
    /// <summary>
    /// Protections the browser enforces, in case anything ever lets foreign code into the page. Only this site's own
    /// scripts (and WebAssembly) run, no other site may show the app in a frame, and files are taken for what their type
    /// says. Style attributes are allowed: the app sets bar widths with them.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; " +
            "style-src-attr 'unsafe-inline'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        // Personal data: neither the browser nor anything on the way may keep a copy of an answer of the API.
        if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
        return next(context);
    });
}
