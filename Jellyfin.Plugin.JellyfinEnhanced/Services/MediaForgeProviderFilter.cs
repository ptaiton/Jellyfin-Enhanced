using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyfinEnhanced.Services;

/// <summary>Use the existing Enhanced request/discovery UI with MediaForge as its provider.</summary>
public sealed class MediaForgeProviderFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var config = JellyfinEnhanced.Instance?.Configuration;
        var path = context.HttpContext.Request.Path.Value ?? "";
        var native = path.StartsWith("/JellyfinEnhanced/jellyseerr/", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/JellyfinEnhanced/arr/", StringComparison.OrdinalIgnoreCase)
                && !path.Equals("/JellyfinEnhanced/arr/calendar/user-data", StringComparison.OrdinalIgnoreCase));
        if (path.Equals("/JellyfinEnhanced/jellyseerr/sync-watchlist", StringComparison.OrdinalIgnoreCase)) native=false;
        if (config?.MediaForgeEnabled != true || !native) { await next(); return; }
        var request = context.HttpContext.Request;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }
        var token = request.Headers["X-Emby-Token"].ToString();
        if (string.IsNullOrEmpty(token) || token.Length > 4096) { context.Result = new UnauthorizedResult(); return; }
        if (!Uri.TryCreate(config.MediaForgeUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https") || root.UserInfo.Length > 0 || root.Query.Length > 0 || root.Fragment.Length > 0)
        { context.Result = new ObjectResult(new { error = "MediaForge URL is invalid." }) { StatusCode = 503 }; return; }
        if (request.ContentLength > 65536) { context.Result = new StatusCodeResult(413); return; }
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
                    var read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), context.HttpContext.RequestAborted);
                    if (read == 0) break;
                    count += read;
                }
                if (count > 65536) { context.Result = new StatusCodeResult(413); return; }
                upstream.Content = new StringContent(new string(buffer, 0, count), System.Text.Encoding.UTF8, "application/json");
            }
            // Mutations must finish even when a search/navigation closes its originating view.
            var cancellation = request.Method == "GET" ? context.HttpContext.RequestAborted : CancellationToken.None;
            using var response = await client.SendAsync(upstream, cancellation);
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new ContentResult {
                StatusCode = (int)response.StatusCode, ContentType = "application/json",
                Content = await response.Content.ReadAsStringAsync(cancellation)
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        { context.Result = new ObjectResult(new { error = true, code = "unreachable", message = "MediaForge is unavailable." }) { StatusCode = 502 }; }
    }
}
