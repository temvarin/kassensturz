namespace Geld.Server.Hosting;

public static class SecurityHeaders
{
    /// <summary>
    /// Protections the browser enforces in case foreign code ever gets into the page: only this site's scripts run, no
    /// other site may frame the app, and files are taken for their declared type. Style attributes stay allowed because
    /// the app sets bar widths with them.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; " +
            "style-src-attr 'unsafe-inline'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        // Personal data, so neither the browser nor anything in between may keep a copy of an API answer.
        if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
        return next(context);
    });
}
