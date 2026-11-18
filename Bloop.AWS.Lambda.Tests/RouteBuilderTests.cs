using Bloop.AWS.Lambda.Routing;
using Xunit;

namespace Bloop.AWS.Lambda.Tests;

public class RouteBuilderTests
{
    private static readonly Func<ApiRequest, Task<ApiResult>> Noop = _ => Task.FromResult(ApiResult.NoContent());

    private static RouteBuilder Routes() => new RouteBuilder()
        .Get("/tasks", Noop)
        .Post("/tasks", Noop)
        .Get("/tasks/{id}", Noop)
        .Get("/tasks/today", Noop)
        .Delete("/tasks/{id}", Noop)
        .Get("/projects/{projectId}/tasks", Noop);

    [Fact]
    public void Match_ExactLiteralPath_SelectsRouteForMethod()
    {
        var match = Routes().Match("POST", "/tasks");

        Assert.Equal("POST", match.Route!.Method);
        Assert.Equal("/tasks", match.Route.Template.Text);
    }

    [Fact]
    public void Match_ParameterSegment_CapturesDecodedValue()
    {
        var match = Routes().Match("GET", "/tasks/a%20b");

        Assert.Equal("/tasks/{id}", match.Route!.Template.Text);
        Assert.Equal("a b", match.Values["id"]);
    }

    [Fact]
    public void Match_LiteralBeatsParameter()
    {
        var match = Routes().Match("GET", "/tasks/today");

        Assert.Equal("/tasks/today", match.Route!.Template.Text);
    }

    [Fact]
    public void Match_NestedParameter_Captured()
    {
        var match = Routes().Match("GET", "/projects/42/tasks/");

        Assert.Equal("/projects/{projectId}/tasks", match.Route!.Template.Text);
        Assert.Equal("42", match.Values["projectId"]);
    }

    [Fact]
    public void Match_EmulatorStagePrefix_IsSkipped()
    {
        var match = Routes().Match("DELETE", "/execute-api/033b4c49a6/$default/tasks/7");

        Assert.Equal("/tasks/{id}", match.Route!.Template.Text);
        Assert.Equal("7", match.Values["id"]);
    }

    [Fact]
    public void Match_MethodIsCaseInsensitive()
    {
        Assert.NotNull(Routes().Match("get", "/tasks").Route);
    }

    [Fact]
    public void Match_KnownPathWrongMethod_ReportsPathMatched()
    {
        var match = Routes().Match("PUT", "/tasks");

        Assert.Null(match.Route);
        Assert.True(match.PathMatched);
    }

    [Fact]
    public void Match_UnknownPath_ReportsNotFound()
    {
        var match = Routes().Match("GET", "/nothing/here");

        Assert.Null(match.Route);
        Assert.False(match.PathMatched);
    }

    [Theory]
    [InlineData("/{id}")]
    [InlineData("/")]
    [InlineData("/tasks/x{id}")]
    [InlineData("/tasks/{id}/sub/{id}")]
    public void Map_InvalidTemplate_Throws(string template)
    {
        Assert.Throws<ArgumentException>(() => new RouteBuilder().Get(template, Noop));
    }
}
