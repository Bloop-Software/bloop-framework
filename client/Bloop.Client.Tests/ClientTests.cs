using Bloop.Client.Desktop;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Xunit;

namespace Bloop.Client.Tests;

public sealed record Widget(string Name, int Size);

[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Widget))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!,
            request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return respond(request);
    }
}

public class ApiClientBaseTests
{
    private sealed class WidgetApi(HttpClient http, ApiEndpoint endpoint) : ApiClientBase(http, endpoint)
    {
        public Task<Widget> GetWidgetAsync() => GetAsync("/widgets/1", TestJsonContext.Default.Widget);

        public Task<Widget> CreateAsync(Widget widget) =>
            SendAsync(HttpMethod.Post, "widgets", widget, TestJsonContext.Default.Widget, TestJsonContext.Default.Widget);

        public Task DeleteAsync() => SendAsync(HttpMethod.Delete, "/widgets/1");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Get_ResolvesAgainstBaseAddressIncludingItsPath_AndReadsCamelCaseJson()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{"name":"Gear","size":3}"""));
        var api = new WidgetApi(new HttpClient(handler), new ApiEndpoint { BaseAddress = new Uri("https://host.example/api/v1") });

        var widget = await api.GetWidgetAsync();

        Assert.Equal(new Widget("Gear", 3), widget);
        Assert.Equal("https://host.example/api/v1/widgets/1", handler.Requests.Single().Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Post_SendsCamelCaseJsonBody()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, """{"name":"Cog","size":5}"""));
        var api = new WidgetApi(new HttpClient(handler), new ApiEndpoint { BaseAddress = new Uri("https://host.example/") });

        await api.CreateAsync(new Widget("Cog", 5));

        Assert.Equal("""{"name":"Cog","size":5}""", handler.Requests.Single().Body);
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task ErrorResponse_ThrowsApiExceptionWithServerDetail()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Conflict, """{"detail":"Already exists"}"""));
        var api = new WidgetApi(new HttpClient(handler), new ApiEndpoint { BaseAddress = new Uri("https://host.example/") });

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.CreateAsync(new Widget("Cog", 5)));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("Already exists", ex.Message);
    }

    [Fact]
    public async Task ErrorResponseWithoutJson_StillThrowsApiException()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway</html>"),
        });
        var api = new WidgetApi(new HttpClient(handler), new ApiEndpoint { BaseAddress = new Uri("https://host.example/") });

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.DeleteAsync());

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public void Endpoint_Unconfigured_ThrowsClearError()
    {
        var endpoint = new ApiEndpoint();

        Assert.False(endpoint.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => endpoint.Resolve("/x"));
    }
}

public class LoopbackAuthBrowserTests
{
    [Fact]
    public async Task Session_CompletesWithQueryParametersFromTheRedirect()
    {
        string? opened = null;
        var browser = new LoopbackAuthBrowser(address => opened = address);
        await using var session = await browser.BeginAsync();

        var waiting = session.OpenAndWaitAsync("https://login.example/authorize?x=1");
        using var http = new HttpClient();
        var page = await http.GetAsync(session.RedirectUri + "?code=abc%20123&state=xyz");
        var result = await waiting;

        Assert.Equal("https://login.example/authorize?x=1", opened);
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/callback$", session.RedirectUri);
        Assert.Equal("abc 123", result["code"]);
        Assert.Equal("xyz", result["STATE"]);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("You are signed in", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Session_ErrorRedirect_IsReturnedToCaller()
    {
        var browser = new LoopbackAuthBrowser(_ => { });
        await using var session = await browser.BeginAsync();

        var waiting = session.OpenAndWaitAsync("https://login.example/authorize");
        using var http = new HttpClient();
        var page = await http.GetStringAsync(session.RedirectUri + "?error=not_allowed");

        Assert.Equal("not_allowed", (await waiting)["error"]);
        Assert.Contains("not completed", page);
    }

    [Fact]
    public async Task Session_NoRedirectInTime_TimesOut()
    {
        var browser = new LoopbackAuthBrowser(_ => { }, TimeSpan.FromMilliseconds(200));
        await using var session = await browser.BeginAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => session.OpenAndWaitAsync("https://login.example/authorize"));
    }

    [Fact]
    public async Task Sessions_UseDifferentPorts()
    {
        var browser = new LoopbackAuthBrowser(_ => { });
        await using var first = await browser.BeginAsync();
        await using var second = await browser.BeginAsync();

        Assert.NotEqual(first.RedirectUri, second.RedirectUri);
    }
}

public class SecureStoreTests
{
    public static TheoryData<string> Stores => new() { "memory", "file" };

    private static readonly string ApplicationName = $"bloop-tests-{Guid.NewGuid():N}";

    private static string ApplicationDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task SetGetRemove_RoundTrips(string kind)
    {
        ISecureStore store = kind == "file" ? new FileSecureStore(ApplicationName) : new InMemorySecureStore();

        Assert.Null(await store.GetAsync("session"));

        await store.SetAsync("session", "first");
        await store.SetAsync("session", "second éè \U0001F512");
        await store.SetAsync("other/key:with odd chars", "x");

        Assert.Equal("second éè \U0001F512", await store.GetAsync("session"));
        Assert.Equal("x", await store.GetAsync("other/key:with odd chars"));

        await store.RemoveAsync("session");
        await store.RemoveAsync("never-set");

        Assert.Null(await store.GetAsync("session"));
        Assert.Equal("x", await store.GetAsync("other/key:with odd chars"));

        if (Directory.Exists(ApplicationDirectory))
            Directory.Delete(ApplicationDirectory, recursive: true);
    }

    [Fact]
    public async Task FileStore_FileIsReadableOnlyByTheUser()
    {
        if (OperatingSystem.IsWindows())
            return;

        var name = $"bloop-tests-{Guid.NewGuid():N}";
        await new FileSecureStore(name).SetAsync("k", "v");

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name, "store");
        var file = Directory.GetFiles(directory).Single();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
    }
}
