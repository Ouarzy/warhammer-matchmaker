namespace Matchmaker.Web;

/// <summary>
/// En-têtes de sécurité de toutes les réponses : le site ne peut pas être affiché dans une page tierce
/// (clickjacking sur « Abandonner », « Confirmer »…) ni charger de scripts venus d'ailleurs.
/// </summary>
public static class SecurityHeaders
{
    // Scripts : 'unsafe-inline' reste nécessaire pour l'import map de Blazor et le petit script de App.razor.
    // Images : avatars Steam. Connexions : le circuit Blazor (WebSocket vers le site lui-même).
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://*.steamstatic.com https://*.akamaihd.net; " +
        "connect-src 'self'{0}; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self' https://steamcommunity.com; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
    {
        // En développement, le rechargement automatique du navigateur passe par un WebSocket sur un autre port.
        var policy = string.Format(ContentSecurityPolicy, app.Environment.IsDevelopment() ? " ws://localhost:* wss://localhost:*" : "");
        return app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = policy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            return next(context);
        });
    }
}
