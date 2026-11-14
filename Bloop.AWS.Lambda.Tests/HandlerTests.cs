using Bloop.WebApi;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Bloop.AWS.Lambda.Tests;

public class HandlerTests
{
    private sealed record EchoRequest(string Text);

    private sealed record EchoResponse(string Text);

    private sealed class EchoHandler : Handler<EchoRequest, EchoResponse>
    {
        protected override Task<WebApiResponse<EchoResponse>> HandleRequestAsync(
            IServiceProvider services, EchoRequest? request)
        {
            if (request == null)
                throw new WebApiException("Invalid request");

            if (request.Text == "boom")
                throw new InvalidOperationException("unexpected");

            return Task.FromResult(new WebApiResponse<EchoResponse>(HttpStatusCode.OK, new EchoResponse(request.Text))
            {
                Headers = { ["X-Echo"] = "1" },
            });
        }
    }

    [Fact]
    public async Task Handle_ValidBody_ReturnsJsonWithCustomHeaders()
    {
        var response = await new EchoHandler().Handle(
            new GatewayEvent { Body = """{"text":"hello"}""" }, new TestLambdaContext());

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal("1", response.Headers["X-Echo"]);
        Assert.Equal("hello", JsonDocument.Parse(response.Body).RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("{broken", "Invalid request body")]
    [InlineData(null, "Invalid request")]
    public async Task Handle_BadBody_Returns400(string? body, string detail)
    {
        var response = await new EchoHandler().Handle(new GatewayEvent { Body = body }, new TestLambdaContext());

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(detail, JsonDocument.Parse(response.Body).RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Handle_UnhandledException_Returns500()
    {
        var response = await new EchoHandler().Handle(
            new GatewayEvent { Body = """{"text":"boom"}""" }, new TestLambdaContext());

        Assert.Equal(500, response.StatusCode);
    }
}
