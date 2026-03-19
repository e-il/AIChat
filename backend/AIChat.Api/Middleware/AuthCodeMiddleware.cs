namespace AIChat.Api.Middleware;

public class AuthCodeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _validCodes;

    public AuthCodeMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var codes = configuration.GetSection("AuthCodes").Get<string[]>() ?? [];
        _validCodes = new HashSet<string>(codes, StringComparer.Ordinal);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for SignalR negotiate (auth handled in hub)
        if (context.Request.Path.StartsWithSegments("/chathub"))
        {
            await _next(context);
            return;
        }

        // Check for auth code in header
        var authCode = context.Request.Headers["X-Auth-Code"].FirstOrDefault();

        if (string.IsNullOrEmpty(authCode) || !_validCodes.Contains(authCode))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing authentication code" });
            return;
        }

        await _next(context);
    }
}

public static class AuthCodeMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthCode(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthCodeMiddleware>();
    }
}
