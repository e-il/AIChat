using AIChat.Api.Services;

namespace AIChat.Api.Middleware;

public class AuthCodeMiddleware
{
    public const string UserIdItemKey = "userId";

    private readonly RequestDelegate _next;

    public AuthCodeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserIdentityService identity)
    {
        // Skip auth for SignalR negotiate (auth handled in hub)
        if (context.Request.Path.StartsWithSegments("/chathub"))
        {
            await _next(context);
            return;
        }

        var authCode = context.Request.Headers["X-Auth-Code"].FirstOrDefault();

        // Fallback: GET /api/images/{file} is loaded by <img src=...> which can't send
        // custom headers. Allow ?access_token=<code> like SignalR does. Scoped tightly to
        // image GETs to avoid broader query-string-token exposure.
        if (string.IsNullOrEmpty(authCode)
            && HttpMethods.IsGet(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api/images"))
        {
            authCode = context.Request.Query["access_token"].FirstOrDefault();
        }

        var userId = identity.ResolveUserId(authCode);

        if (userId is null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing authentication code" });
            return;
        }

        context.Items[UserIdItemKey] = userId;
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
