using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyfinEnhanced.Services;

/// <summary>Route native Enhanced requests before MVC, including on hosts that
/// construct their MVC options before plugin service registration. MediaForge
/// validates every forwarded Jellyfin token; this does not rely on an already
/// authenticated principal or forward any server credential.</summary>
public sealed class MediaForgeProviderStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(InvokeAsync);
        next(app);
    };

    internal static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var config = JellyfinEnhanced.Instance?.Configuration;
        var path = context.Request.Path.Value ?? "";
        // Startup filters run before Jellyfin's BaseUrl/authentication middleware.
        // Match the routed suffix, then let MediaForge verify the personal token.
        var prefix = path.IndexOf("/JellyfinEnhanced/", StringComparison.OrdinalIgnoreCase);
        if (prefix < 0) { await next(); return; }
        path = path[prefix..];
        var native = path.StartsWith("/JellyfinEnhanced/jellyseerr/", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/JellyfinEnhanced/arr/", StringComparison.OrdinalIgnoreCase)
                && !path.Equals("/JellyfinEnhanced/arr/calendar/user-data", StringComparison.OrdinalIgnoreCase));
        if (path.Equals("/JellyfinEnhanced/jellyseerr/sync-watchlist", StringComparison.OrdinalIgnoreCase)) native=false;
        if (config?.MediaForgeEnabled != true || !native) { await next(); return; }
        var request = context.Request;
        var token = request.Headers["X-Emby-Token"].ToString();
        if (string.IsNullOrEmpty(token) || token.Length > 4096) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "A Jellyfin user session is required." }); return; }
        if (!Uri.TryCreate(config.MediaForgeUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https") || root.UserInfo.Length > 0 || root.Query.Length > 0 || root.Fragment.Length > 0)
        { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "MediaForge URL is invalid." }); return; }
        if (request.ContentLength > 65536) { context.Response.StatusCode = 413; return; }
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
            var relative = path["/JellyfinEnhanced/".Length..];
            using var upstream = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(root,
                "api/integrations/jellyfin-enhanced/" + relative + request.QueryString));
            upstream.Headers.Add("X-MediaForge-Jellyfin-Token", token);
            upstream.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (request.Method != "GET" && request.Method != "HEAD")
            {
                using var reader = new StreamReader(request.Body);
                var buffer = new char[65537];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), context.RequestAborted);
                    if (read == 0) break;
                    count += read;
                }
                if (count > 65536) { context.Response.StatusCode = 413; return; }
                upstream.Content = new StringContent(new string(buffer, 0, count), System.Text.Encoding.UTF8, "application/json");
            }
            // Mutations must finish even when a search/navigation closes its originating view.
            var cancellation = request.Method == "GET" ? context.RequestAborted : CancellationToken.None;
            using var response = await client.SendAsync(upstream, cancellation);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(await response.Content.ReadAsStringAsync(cancellation), cancellation);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        { context.Response.StatusCode = 502; await context.Response.WriteAsJsonAsync(new { error = true, code = "unreachable", message = "MediaForge is unavailable." }); }
    }
}
