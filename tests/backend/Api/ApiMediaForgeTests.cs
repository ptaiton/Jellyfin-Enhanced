using System.Security.Claims;
using Jellyfin.Plugin.JellyfinEnhanced.Controllers;
using Jellyfin.Plugin.JellyfinEnhanced.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace JE.Tests;
[Collection("Plugin singleton")]
public class ApiMediaForgeTests
{
    private static ResourceExecutingContext Context(string path, bool signedIn=true)
    {
        var http=new DefaultHttpContext();http.Request.Path=path;http.Request.Method="GET";
        if(signedIn)http.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString())},"test"));
        return new ResourceExecutingContext(new ActionContext(http,new RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),new List<IFilterMetadata>(),new List<IValueProviderFactory>());
    }
    [Fact]
    public async Task MediaForgeReusesOriginalSeerrRoutesWithoutExposingAPanel()
    {
        using var fixture=new ApiPluginFixture();fixture.Plugin.Configuration.MediaForgeEnabled=true;
        var probe=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);probe.Start();var port=((System.Net.IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new System.Net.HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        fixture.Plugin.Configuration.MediaForgeUrl=$"http://127.0.0.1:{port}/base";
        var context=Context("/JellyfinEnhanced/jellyseerr/search");context.HttpContext.Request.QueryString=new QueryString("?query=Dune&page=2");context.HttpContext.Request.Headers["X-Emby-Token"]="personal-session";
        var pending=new MediaForgeProviderFilter().OnResourceExecutionAsync(context,()=>throw new Exception("Must not contact Seerr"));
        var received=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("/base/api/integrations/jellyfin-enhanced/jellyseerr/search",received.Request.Url!.AbsolutePath);
        Assert.Equal("Dune",received.Request.QueryString["query"]);
        Assert.Equal("2",received.Request.QueryString["page"]);
        Assert.Equal("personal-session",received.Request.Headers["X-MediaForge-Jellyfin-Token"]);
        Assert.Null(received.Request.Headers["Cookie"]);
        var bytes=System.Text.Encoding.UTF8.GetBytes("{\"results\":[]}");received.Response.ContentLength64=bytes.Length;await received.Response.OutputStream.WriteAsync(bytes);received.Response.Close();await pending;
        Assert.Equal(200,Assert.IsType<ContentResult>(context.Result).StatusCode);
    }
    [Theory]
    [InlineData("/JellyfinEnhanced/jellyseerr/search")]
    [InlineData("/JellyfinEnhanced/arr/queue")]
    public async Task NativeProviderRequiresAnAuthenticatedPersonalSession(string path)
    {
        using var fixture=new ApiPluginFixture();fixture.Plugin.Configuration.MediaForgeEnabled=true;
        foreach(var authenticated in new[]{false,true}){
            var context=Context(path,authenticated);
            await new MediaForgeProviderFilter().OnResourceExecutionAsync(context,()=>throw new Exception("Must reject"));
            Assert.IsType<UnauthorizedResult>(context.Result);
        }
    }
    [Fact]
    public async Task DisabledProviderLeavesSeerrAndOtherEndpointsUntouched()
    {
        using var fixture=new ApiPluginFixture();fixture.Plugin.Configuration.MediaForgeEnabled=false;
        var context=Context("/JellyfinEnhanced/jellyseerr/search");var called=false;
        await new MediaForgeProviderFilter().OnResourceExecutionAsync(context,()=>{called=true;return Task.FromResult(new ResourceExecutedContext(context,new List<IFilterMetadata>()));});
        Assert.True(called);
    }
}
