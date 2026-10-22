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
    public record LoginRequest(string Username, string Password);

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

    [HttpPost("login")]
    [RequestSizeLimit(16384)]
    public Task<IActionResult> Login([FromBody] LoginRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || body.Username.Length > 256 || (body.Password == null || body.Password.Length > 4096))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Invalid credentials." }));
        return Send(HttpMethod.Post, "api/auth/login", JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), true);
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

    [HttpPost("logout")]
    public Task<IActionResult> Logout() => Send(HttpMethod.Post, "api/auth/logout");

    private async Task<IActionResult> Send(HttpMethod method, string path, string? body = null, bool login = false)
    {
        var url = BaseUrl();
        if (url == null) return StatusCode(503, new { error = "MediaForge is not configured." });
        var session = Request.Headers["X-MediaForge-Session"].ToString();
        if (!login && (session.Length == 0 || session.Length > 8192 || session.Any(c => !char.IsAsciiHexDigit(c) && c != '.')))
            return Unauthorized(new { error = "Sign in to MediaForge." });
        try
        {
            // Never follow redirects with credentials, persist cookies, or forward Jellyfin auth headers.
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
            using var request = new HttpRequestMessage(method, new Uri(url, path));
            if (!login) request.Headers.Add("Cookie", "session=" + session);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpContext.RequestAborted);
            if (login && response.IsSuccessStatusCode)
            {
                var cookies = new CookieContainer();
                if (response.Headers.TryGetValues("Set-Cookie", out var values))
                    foreach (var value in values) cookies.SetCookies(url, value);
                var token = cookies.GetCookies(url)["session"]?.Value;
                if (string.IsNullOrEmpty(token)) return StatusCode(502, new { error = "MediaForge did not return a session." });
                Response.Headers.CacheControl = "no-store";
                return Ok(new { session = token });
            }
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode is >= 400 and < 500 ? (int)response.StatusCode : 502,
                    new { error = response.StatusCode == HttpStatusCode.Unauthorized ? "Invalid or expired MediaForge session. Sign in again." : "MediaForge could not complete this operation." });
            Response.Headers.CacheControl = "no-store";
            return Content(await response.Content.ReadAsStringAsync(HttpContext.RequestAborted), "application/json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CookieException)
        {
            return StatusCode(502, new { error = "Unable to contact MediaForge." });
        }
    }
}
