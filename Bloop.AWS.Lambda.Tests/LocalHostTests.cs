using Bloop.AWS.Lambda.LocalHost;
using Bloop.AWS.Lambda.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Bloop.AWS.Lambda.Tests;

public class LocalHostTests : IAsyncLifetime
{
    private sealed record Note(string Text);

    private sealed class NotesHandler : RoutedHandler
    {
        protected override void MapRoutes(RouteBuilder routes)
        {
            routes.Get("/notes/{id}", request => Task.FromResult(ApiResult.Ok(new
            {
                Id = request.Route("id"),
                Search = request.Query.GetValueOrDefault("q"),
                Agent = request.Header("User-Agent"),
                Ip = request.Services.GetRequiredService<IHttpContextAccessor>()
                    .HttpContext!.Connection.RemoteIpAddress?.ToString(),
            })));

            routes.Post<Note>("/notes", (_, note) => Task.FromResult(ApiResult.Created(note)));

            routes.Get("/notes/go", _ => Task.FromResult(ApiResult.Redirect("https://example.com/next")));
        }
    }

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapLambda("/notes", "notes-api", new NotesHandler().Handle));
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync() => await _host.StopAsync();

    [Fact]
    public async Task Get_PassesPathQueryAndHeadersToHandler()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/notes/42?q=hello%20world");
        request.Headers.UserAgent.ParseAdd("local-tests/1.0");

        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("42", json.GetProperty("id").GetString());
        Assert.Equal("hello world", json.GetProperty("search").GetString());
        Assert.Equal("local-tests/1.0", json.GetProperty("agent").GetString());
    }

    [Fact]
    public async Task Post_ForwardsJsonBody()
    {
        var response = await _client.PostAsJsonAsync("/notes", new Note("remember"));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("remember", json.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Redirect_IsReturnedToCaller()
    {
        var response = await _client.GetAsync("/notes/go");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/next", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task UnknownRouteUnderPrefix_Returns404FromHandler()
    {
        var response = await _client.GetAsync("/notes/a/b/c");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found", json.GetProperty("detail").GetString());
    }
}
