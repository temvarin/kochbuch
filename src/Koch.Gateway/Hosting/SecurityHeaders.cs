namespace Koch.Gateway.Hosting;

public static class SecurityHeaders
{
    /// <summary>
    /// Protections the browser enforces in case foreign code ever gets into the page: only this site's scripts run, no
    /// other site may frame the app, and files are taken for their declared type. Ported from kassensturz, with two
    /// additions for photos: uploads go straight to Blob Storage (<paramref name="uploadOrigin"/> in connect-src), and
    /// the edit form previews a chosen photo as a data: URL.
    /// </summary>
    /// <param name="uploadOrigin">The Blob endpoint, e.g. https://stkochbuch.blob.core.windows.net; null without photos.</param>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string? uploadOrigin) => app.Use((context, next) =>
    {
        var headers = context.Response.Headers;
        var connect = uploadOrigin is null ? "'self'" : $"'self' {uploadOrigin}";
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; " +
            $"style-src-attr 'unsafe-inline'; img-src 'self' data:; connect-src {connect}; object-src 'none'; base-uri 'self'; " +
            "form-action 'self'; frame-ancestors 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        // The household's data, so neither the browser nor anything in between may keep a copy of an API answer.
        if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
        return next(context);
    });
}
