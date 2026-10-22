using System.Net;
using Jellyfin.Plugin.JellyfinEnhanced.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace JE.Tests;
[Collection("Plugin singleton")]
public class ApiMediaForgeTests
{
    private static TestServer Server() => new(new WebHostBuilder().Configure(app =>
        new MediaForgeProviderStartupFilter().Configure(builder => builder.Run(async context =>
        {
            context.Response.StatusCode=503;
            await context.Response.WriteAsync("legacy Seerr controller");
        }))(app)));

    [Theory]
    [InlineData("")]
    [InlineData("/jellyfin")]
    public async Task StartupRoutingWorksBeforeMvcAndAuthenticationIncludingBaseUrl(string basePath)
    {
        using var fixture=new ApiPluginFixture();fixture.Plugin.Configuration.MediaForgeEnabled=true;
        var probe=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        fixture.Plugin.Configuration.MediaForgeUrl=$"http://127.0.0.1:{port}/mediaforge";
        using var server=Server();using var client=server.CreateClient();
        using var request=new HttpRequestMessage(HttpMethod.Get,basePath+"/JellyfinEnhanced/jellyseerr/search?query=clayface&page=2");
        request.Headers.Add("X-Emby-Token","personal-session");request.Headers.Add("Cookie","unrelated=cookie");
        var pending=client.SendAsync(request);
        var received=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("/mediaforge/api/integrations/jellyfin-enhanced/jellyseerr/search",received.Request.Url!.AbsolutePath);
        Assert.Equal("clayface",received.Request.QueryString["query"]);
        Assert.Equal("2",received.Request.QueryString["page"]);
        Assert.Equal("personal-session",received.Request.Headers["X-MediaForge-Jellyfin-Token"]);
        Assert.Null(received.Request.Headers["Cookie"]);
        Assert.Null(received.Request.Headers["X-MediaForge-Server-Key"]);
        var bytes=System.Text.Encoding.UTF8.GetBytes("{\"results\":[{\"title\":\"Clayface\"}]}");received.Response.ContentLength64=bytes.Length;await received.Response.OutputStream.WriteAsync(bytes);received.Response.Close();
        using var response=await pending;Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("Clayface",await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("/JellyfinEnhanced/jellyseerr/user-status")]
    [InlineData("/JellyfinEnhanced/arr/queue")]
    public async Task MissingPersonalSessionNeverReachesLegacyController(string path)
    {
        using var fixture=new ApiPluginFixture();fixture.Plugin.Configuration.MediaForgeEnabled=true;
        using var server=Server();using var client=server.CreateClient();using var response=await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
    [Fact]
    public async Task DisabledProviderLeavesOriginalSeerrEndpointUntouched()
    {
        using var fixture=new ApiPluginFixture();fixture.Plugin.Configuration.MediaForgeEnabled=false;
        using var server=Server();using var client=server.CreateClient();using var response=await client.GetAsync("/JellyfinEnhanced/jellyseerr/search");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Equal("legacy Seerr controller",await response.Content.ReadAsStringAsync());
    }
}
