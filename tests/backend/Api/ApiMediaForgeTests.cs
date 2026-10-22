using Jellyfin.Plugin.JellyfinEnhanced.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JE.Tests;

[Collection("Plugin singleton")]
public class ApiMediaForgeTests
{
    private static MediaForgeController Controller() => new()
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public void AllRoutesRequireJellyfinAuthentication()
    {
        Assert.NotEmpty(typeof(MediaForgeController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.DoesNotContain(typeof(MediaForgeController).GetMethods(), m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@mediaforge.test")]
    [InlineData("https://mediaforge.test?token=secret")]
    [InlineData("https://mediaforge.test/#fragment")]
    public async Task InvalidServerAddressesFailClosed(string url)
    {
        using var fixture = new ApiPluginFixture();
        fixture.Plugin.Configuration.MediaForgeEnabled = true;
        fixture.Plugin.Configuration.MediaForgeUrl = url;
        var result = Assert.IsType<ObjectResult>(await Controller().Defaults());
        Assert.Equal(503, result.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("token; injected=cookie")]
    [InlineData("token\r\nX-Emby-Token: secret")]
    public async Task MissingOrMalformedSessionsAreRejectedBeforeUpstream(string session)
    {
        using var fixture = new ApiPluginFixture();
        fixture.Plugin.Configuration.MediaForgeEnabled = true;
        fixture.Plugin.Configuration.MediaForgeUrl = "http://127.0.0.1:1";
        var controller = Controller();
        controller.Request.Headers["X-MediaForge-Session"] = session;
        Assert.IsType<UnauthorizedObjectResult>(await controller.Defaults());
    }

    [Theory]
    [InlineData("episode", 10)]
    [InlineData("movie", 0)]
    [InlineData("tv", -1)]
    public async Task InvalidDetailTargetsAreRejected(string format, long id)
    {
        Assert.IsType<BadRequestResult>(await Controller().Details(format, id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProxyUsesConfiguredBasePathAndOnlyPersonalCookie(bool login)
    {
        using var fixture = new ApiPluginFixture();
        var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        fixture.Plugin.Configuration.MediaForgeEnabled = true;
        fixture.Plugin.Configuration.MediaForgeUrl = $"http://127.0.0.1:{port}/base";
        var controller = Controller();
        controller.Request.Headers["X-MediaForge-Session"] = "abc.def";
        controller.Request.Headers["X-Emby-Token"] = "must-not-be-forwarded";
        var pending = login ? controller.Login(new("alice", "secret")) : controller.Search("A & B");
        var upstream = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(upstream.Request.Headers["X-Emby-Token"]);
        if (login)
        {
            Assert.Equal("/base/api/auth/login", upstream.Request.Url!.AbsolutePath);
            Assert.Null(upstream.Request.Headers["Cookie"]);
            using var reader = new StreamReader(upstream.Request.InputStream);
            var payload = System.Text.Json.JsonDocument.Parse(await reader.ReadToEndAsync());
            Assert.Equal("alice", payload.RootElement.GetProperty("username").GetString());
            upstream.Response.Headers.Add("Set-Cookie", "session=123.abc; Path=/; HttpOnly; SameSite=Strict");
        }
        else
        {
            Assert.Equal("/base/api/discover/search", upstream.Request.Url!.AbsolutePath);
            Assert.Equal("A & B", upstream.Request.QueryString["q"]);
            Assert.Equal("session=abc.def", upstream.Request.Headers["Cookie"]);
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes("[]");
        upstream.Response.ContentLength64 = bytes.Length;
        await upstream.Response.OutputStream.WriteAsync(bytes);
        upstream.Response.Close();
        var result = await pending;
        if (login)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("123.abc", System.Text.Json.JsonSerializer.Serialize(ok.Value));
        }
        else Assert.Equal("[]", Assert.IsType<ContentResult>(result).Content);
    }

}
