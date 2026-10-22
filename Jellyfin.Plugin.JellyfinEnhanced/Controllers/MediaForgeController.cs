using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyfinEnhanced.Controllers;

/// <summary>Restricted per-user MediaForge proxy. No administrator credential or arbitrary upstream path.</summary>
[ApiController]
[Authorize]
[Route("JellyfinEnhanced/mediaforge")]
public class MediaForgeController : ControllerBase
{
    [HttpGet("config")]
    public IActionResult Config() => Ok(new { enabled = BaseUrl() != null });

    private static Uri? BaseUrl()
    {
        var config = JellyfinEnhanced.Instance?.Configuration;
        if (config?.MediaForgeEnabled != true || !Uri.TryCreate(config.MediaForgeUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
        return uri;
    }

    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) return Task.FromResult<IActionResult>(BadRequest());
        return Send(HttpMethod.Get, "api/discover/search?q=" + Uri.EscapeDataString(query));
    }

    [HttpGet("defaults")]
    public Task<IActionResult> Defaults() => Send(HttpMethod.Get, "api/discover/config");

    [HttpGet("details/{format}/{id:long}")]
    public Task<IActionResult> Details(string format, long id)
    {
        if ((format != "movie" && format != "tv") || id <= 0) return Task.FromResult<IActionResult>(BadRequest());
        return Send(HttpMethod.Get, $"api/discover/details/{format}/{id}");
    }

    [HttpGet("library")]
    public Task<IActionResult> Library([FromQuery] int page = 1) => Send(HttpMethod.Get, $"api/library?page={Math.Max(1, page)}");

    [HttpPost("request")]
    [RequestSizeLimit(32768)]
    public Task<IActionResult> RequestMedia([FromBody] JsonElement body) => Send(HttpMethod.Post, "api/discover/request", body.GetRawText());

    private async Task<IActionResult> Send(HttpMethod method, string path, string? body = null)
    {
        var url = BaseUrl();
        if (url == null) return StatusCode(503, new { error = "MediaForge is not configured." });
        var token = Request.Headers["X-Emby-Token"].ToString();
        if (string.IsNullOrEmpty(token) || token.Length > 4096)
            return Unauthorized(new { error = "A Jellyfin user session is required." });
        try
        {
            // Forward the current user session only to the configured MediaForge server.
            // MediaForge validates it with its configured Jellyfin /Users/Me on every request.
            // Never follow redirects with credentials or persist cookies.
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
            using var request = new HttpRequestMessage(method, new Uri(url, path));
            request.Headers.Add("X-MediaForge-Jellyfin-Token", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode is >= 400 and < 500 ? (int)response.StatusCode : 502,
                    new { error = response.StatusCode == HttpStatusCode.Unauthorized ? "Your Jellyfin session has expired. Sign in to Jellyfin again." : "MediaForge could not complete this operation. Ensure your Jellyfin account is imported and enabled in MediaForge." });
            Response.Headers.CacheControl = "no-store";
            return Content(await response.Content.ReadAsStringAsync(HttpContext.RequestAborted), "application/json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException )
        {
            return StatusCode(502, new { error = "Unable to contact MediaForge." });
        }
    }
}
