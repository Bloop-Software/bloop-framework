using System.Net;
using System.Text;

namespace Bloop.OAuth.Tests;

internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

    public StubHttpHandler RespondJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        return this;
    }

    public StubHttpHandler RespondText(string text, HttpStatusCode status)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(text) });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body));

        if (_responses.Count == 0)
            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");

        return _responses.Dequeue()(request);
    }
}
