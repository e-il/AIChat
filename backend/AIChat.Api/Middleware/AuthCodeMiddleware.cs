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

        // Public media serving: GET /api/images/{filename}, /api/videos/{filename},
        // or /api/media/{filename} is loaded by <img>/<video src=...> and
        // is left unauthenticated on purpose — filenames are unguessable 128-bit GUIDs.
        // Only GETs are public; upload/generate POSTs still require auth.
        if (HttpMethods.IsGet(context.Request.Method)
            && IsPublicMediaPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var authCode = context.Request.Headers["X-Auth-Code"].FirstOrDefault();

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

    private static bool IsPublicMediaPath(PathString path) =>
        path.StartsWithSegments("/api/images")
        || path.StartsWithSegments("/api/videos")
        || path.StartsWithSegments("/api/media");
}

public static class AuthCodeMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthCode(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthCodeMiddleware>();
    }
}
