using Amazon.Lambda.APIGatewayEvents;
using Bloop.AWS.Lambda.Routing;
using Bloop.WebApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Bloop.AWS.Lambda.Tests;

public class RoutedHandlerTests
{
    private sealed record CreateItem(string Name, int Quantity);

    private sealed record ItemQuery(int Page = 1, string? Tag = null);

    private sealed class Marker(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class ItemsHandler : RoutedHandler
    {
        protected override void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton(new Marker("items"));

        protected override void MapRoutes(RouteBuilder routes)
        {
            routes.Get("/items", request =>
            {
                var query = request.BindQuery<ItemQuery>();
                return Task.FromResult(ApiResult.Ok(new { query.Page, query.Tag }));
            });

            routes.Get("/items/{id}", request => Task.FromResult(ApiResult.Ok(new
            {
                Id = request.RouteGuid("id"),
                Marker = request.Services.GetRequiredService<Marker>().Value,
                Agent = request.Services.GetRequiredService<IHttpContextAccessor>()
                    .HttpContext!.Request.Headers.UserAgent.ToString(),
            })));

            routes.Post<CreateItem>("/items", (_, body) =>
                Task.FromResult(ApiResult.Created(new { body.Name, body.Quantity })));

            routes.Get("/items/callback", _ =>
                Task.FromResult(ApiResult.Redirect("myapp://done?code=1")));

            routes.Get("/items/health", _ =>
                Task.FromResult(ApiResult.Content(HttpStatusCode.OK, "text/plain", "ok")));

            routes.Get("/items/teapot", _ =>
                throw new WebApiException((HttpStatusCode)418, "I am a teapot"));

            routes.Get("/items/boom", _ => throw new InvalidOperationException("secret detail"));

            routes.Delete("/items/{id}", _ => Task.FromResult(ApiResult.NoContent()));
        }
    }

    private sealed class OtherHandler : RoutedHandler
    {
        protected override void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton(new Marker("other"));

        protected override void MapRoutes(RouteBuilder routes) =>
            routes.Get("/other", request => Task.FromResult(
                ApiResult.Ok(new { Marker = request.Services.GetRequiredService<Marker>().Value })));
    }

    private static GatewayEvent Event(string method, string path, string? body = null,
        Dictionary<string, string>? query = null, Dictionary<string, string>? headers = null) => new()
        {
            RawPath = path,
            Body = body,
            QueryStringParameters = query,
            Headers = headers,
            RequestContext = new GatewayRequestContext { Http = new GatewayRequestHttp { Method = method, Path = path } },
        };

    private static Task<APIGatewayHttpApiV2ProxyResponse> Send(RoutedHandler handler, GatewayEvent request,
        TestLambdaContext? context = null) =>
        handler.Handle(request, context ?? new TestLambdaContext());

    private static JsonElement Json(APIGatewayHttpApiV2ProxyResponse response) =>
        JsonDocument.Parse(response.Body).RootElement;

    [Fact]
    public async Task Handle_GetWithRouteValue_ReturnsCamelCaseJson()
    {
        var id = Guid.NewGuid();

        var response = await Send(new ItemsHandler(),
            Event("GET", $"/items/{id}", headers: new() { ["user-agent"] = "tests/1.0" }));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal(id, Json(response).GetProperty("id").GetGuid());
        Assert.Equal("items", Json(response).GetProperty("marker").GetString());
        Assert.Equal("tests/1.0", Json(response).GetProperty("agent").GetString());
    }

    [Fact]
    public async Task Handle_QueryString_IsBound()
    {
        var response = await Send(new ItemsHandler(),
            Event("GET", "/items", query: new() { ["page"] = "3", ["tag"] = "x" }));

        Assert.Equal(3, Json(response).GetProperty("page").GetInt32());
        Assert.Equal("x", Json(response).GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Handle_RawQueryStringOnly_IsParsed()
    {
        var request = Event("GET", "/items");
        request.RawQueryString = "page=4&tag=a%20b";

        var response = await Send(new ItemsHandler(), request);

        Assert.Equal(4, Json(response).GetProperty("page").GetInt32());
        Assert.Equal("a b", Json(response).GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Handle_PostBody_IsDeserialised()
    {
        var response = await Send(new ItemsHandler(),
            Event("POST", "/items", """{"name":"Widget","quantity":2}"""));

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("Widget", Json(response).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Handle_Base64Body_IsDecoded()
    {
        var request = Event("POST", "/items",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"name":"Encoded","quantity":1}""")));
        request.IsBase64Encoded = true;

        var response = await Send(new ItemsHandler(), request);

        Assert.Equal("Encoded", Json(response).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(null, "Request body is required")]
    [InlineData("{not json", "Invalid request body")]
    public async Task Handle_BadBody_Returns400(string? body, string detail)
    {
        var response = await Send(new ItemsHandler(), Event("POST", "/items", body));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(detail, Json(response).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Handle_InvalidRouteGuid_Returns400()
    {
        var response = await Send(new ItemsHandler(), Event("GET", "/items/not-a-guid"));

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task Handle_Redirect_SetsLocationWithoutBody()
    {
        var response = await Send(new ItemsHandler(), Event("GET", "/items/callback"));

        Assert.Equal(302, response.StatusCode);
        Assert.Equal("myapp://done?code=1", response.Headers["Location"]);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task Handle_Content_UsesGivenContentType()
    {
        var response = await Send(new ItemsHandler(), Event("GET", "/items/health"));

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
        Assert.Equal("ok", response.Body);
    }

    [Fact]
    public async Task Handle_NoContent_Returns204()
    {
        var response = await Send(new ItemsHandler(), Event("DELETE", $"/items/{Guid.NewGuid()}"));

        Assert.Equal(204, response.StatusCode);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task Handle_UnknownPath_Returns404()
    {
        var response = await Send(new ItemsHandler(), Event("GET", "/missing"));

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Not found", Json(response).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Handle_WrongMethod_Returns405()
    {
        var response = await Send(new ItemsHandler(), Event("PATCH", "/items"));

        Assert.Equal(405, response.StatusCode);
    }

    [Fact]
    public async Task Handle_WebApiException_ReturnsItsStatusAndDetail()
    {
        var response = await Send(new ItemsHandler(), Event("GET", "/items/teapot"));

        Assert.Equal(418, response.StatusCode);
        Assert.Equal("I am a teapot", Json(response).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Handle_UnhandledException_Returns500WithoutLeakingDetail()
    {
        var context = new TestLambdaContext();

        var response = await Send(new ItemsHandler(), Event("GET", "/items/boom"), context);

        Assert.Equal(500, response.StatusCode);
        Assert.Equal("An error occurred", Json(response).GetProperty("detail").GetString());
        Assert.Contains(context.Errors, e => e.Contains("secret detail"));
    }

    [Fact]
    public async Task Handle_V1Event_UsesTopLevelMethodAndPath()
    {
        var request = new GatewayEvent { Path = "/stage/items/health", HttpMethod = "GET" };

        var response = await Send(new ItemsHandler(), request);

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task Handle_DifferentHandlerTypes_GetSeparateServiceProviders()
    {
        var items = await Send(new ItemsHandler(), Event("GET", $"/items/{Guid.NewGuid()}"));
        var other = await Send(new OtherHandler(), Event("GET", "/other"));

        Assert.Equal("items", Json(items).GetProperty("marker").GetString());
        Assert.Equal("other", Json(other).GetProperty("marker").GetString());
    }

    [Fact]
    public async Task Handle_ProtectedRouteWithoutToken_Returns401()
    {
        var response = await Send(new SecureHandler(), Event("GET", "/secure"));

        Assert.Equal(401, response.StatusCode);
    }

    [Fact]
    public async Task Handle_ProtectedRouteWithToken_ValidatesSessionAndReturnsUser()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var token = JwtTokenValidatorTests.CreateToken(userId, sessionId);

        var response = await Send(new SecureHandler(),
            Event("GET", "/secure", headers: new() { ["authorization"] = $"Bearer {token}" }));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(userId, Json(response).GetProperty("userId").GetGuid());
        Assert.Equal((userId, sessionId), SecureHandler.Sessions.Validated.Single());
    }

    private sealed class RecordingSessionValidator : ISessionValidator
    {
        public List<(Guid UserId, Guid SessionId)> Validated { get; } = [];

        public Task ValidateSessionAsync(Guid userId, Guid sessionId)
        {
            Validated.Add((userId, sessionId));
            return Task.CompletedTask;
        }
    }

    private sealed class SecureHandler : RoutedHandler
    {
        public static readonly RecordingSessionValidator Sessions = new();

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(new JwtTokenValidator(
                JwtTokenValidatorTests.Issuer, JwtTokenValidatorTests.Audience, JwtTokenValidatorTests.PrivateKey));
            services.AddSingleton<ISessionValidator>(Sessions);
        }

        protected override void MapRoutes(RouteBuilder routes) =>
            routes.Get("/secure", async request =>
                ApiResult.Ok(new { UserId = await request.RequireUserIdAsync() }));
    }
}
