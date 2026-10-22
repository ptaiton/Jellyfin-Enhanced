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
    [InlineData(null)]

    public async Task MissingOrMalformedSessionsAreRejectedBeforeUpstream(string? session)
    {
        using var fixture = new ApiPluginFixture();
        fixture.Plugin.Configuration.MediaForgeEnabled = true;
        fixture.Plugin.Configuration.MediaForgeUrl = "http://127.0.0.1:1";
        var controller = Controller();
        controller.Request.Headers["X-Emby-Token"] = session;
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
    [Fact]
    public async Task ProxyForwardsCurrentJellyfinSessionToFixedMediaForgeRoute()
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
        controller.Request.Headers["X-Emby-Token"] = "personal-jellyfin-token";
        controller.Request.Headers["Cookie"] = "unrelated=cookie";
        var pending = controller.Search("A & B");
        var upstream = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("personal-jellyfin-token", upstream.Request.Headers["X-MediaForge-Jellyfin-Token"]);
        Assert.Null(upstream.Request.Headers["X-Emby-Token"]);
        Assert.Null(upstream.Request.Headers["Cookie"]);
        Assert.Equal("/base/api/discover/search", upstream.Request.Url!.AbsolutePath);
        Assert.Equal("A & B", upstream.Request.QueryString["q"]);
        var bytes = System.Text.Encoding.UTF8.GetBytes("[]");
        upstream.Response.ContentLength64 = bytes.Length;
        await upstream.Response.OutputStream.WriteAsync(bytes);
        upstream.Response.Close();
        Assert.Equal("[]", Assert.IsType<ContentResult>(await pending).Content);
    }
}
