using Bloop.AWS.Lambda.Routing;
using Bloop.WebApi;
using Xunit;

namespace Bloop.AWS.Lambda.Tests;

public class QueryBinderTests
{
    private enum Status { Todo, Done }

    private sealed record Filter(
        DateTime From,
        Guid? ProjectId,
        string? Search,
        Status[]? Statuses,
        int Limit = 50,
        bool IncludeArchived = false,
        DateOnly? DueOn = null);

    private static Filter Bind(params (string Key, string Value)[] pairs)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
            query[key] = value;

        return QueryBinder.Bind<Filter>(query);
    }

    [Fact]
    public void Bind_AllValuesPresent_ConvertsEachType()
    {
        var projectId = Guid.NewGuid();

        var filter = Bind(
            ("from", "2026-11-14T08:30:00Z"),
            ("projectId", projectId.ToString()),
            ("search", "invoice"),
            ("statuses", "todo, Done"),
            ("limit", "10"),
            ("includeArchived", "true"),
            ("dueOn", "2026-12-01"));

        Assert.Equal(new DateTime(2026, 11, 14, 8, 30, 0, DateTimeKind.Utc), filter.From);
        Assert.Equal(DateTimeKind.Utc, filter.From.Kind);
        Assert.Equal(projectId, filter.ProjectId);
        Assert.Equal("invoice", filter.Search);
        Assert.Equal([Status.Todo, Status.Done], filter.Statuses);
        Assert.Equal(10, filter.Limit);
        Assert.True(filter.IncludeArchived);
        Assert.Equal(new DateOnly(2026, 12, 1), filter.DueOn);
    }

    [Fact]
    public void Bind_OffsetDateTime_IsNormalisedToUtc()
    {
        var filter = Bind(("from", "2026-11-14T10:30:00+02:00"));

        Assert.Equal(new DateTime(2026, 11, 14, 8, 30, 0, DateTimeKind.Utc), filter.From);
    }

    [Fact]
    public void Bind_OptionalValuesMissing_UsesNullsAndDefaults()
    {
        var filter = Bind(("FROM", "2026-11-14"));

        Assert.Null(filter.ProjectId);
        Assert.Null(filter.Search);
        Assert.Null(filter.Statuses);
        Assert.Equal(50, filter.Limit);
        Assert.False(filter.IncludeArchived);
        Assert.Null(filter.DueOn);
    }

    [Fact]
    public void Bind_RequiredValueMissing_ThrowsBadRequest()
    {
        var ex = Assert.Throws<WebApiException>(() => Bind(("limit", "5")));

        Assert.Contains("'From' is required", ex.Message);
    }

    [Theory]
    [InlineData("limit", "ten")]
    [InlineData("projectId", "not-a-guid")]
    [InlineData("statuses", "todo,unknown")]
    [InlineData("statuses", "7")]
    [InlineData("from", "yesterday")]
    public void Bind_InvalidValue_ThrowsBadRequest(string key, string value)
    {
        var ex = Assert.Throws<WebApiException>(() => Bind(("from", "2026-11-14"), (key, value)));

        Assert.Contains("is invalid", ex.Message);
    }
}
