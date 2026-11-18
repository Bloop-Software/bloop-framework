using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Bloop.IntegrationTests.Infrastructure;

public sealed class GatewayClient(HttpClient httpClient, UserTokenStore users)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string ActingAs { get; set; } = TestUsers.Anonymous;

    public Task<T> PostAsync<T>(string path, object request, string? actingAs = null) =>
        SendAsync<T>(HttpMethod.Post, path, request, actingAs);

    public Task<HttpResponseMessage> PostRawAsync(string path, object request, string? actingAs = null) =>
        SendRawAsync(HttpMethod.Post, path, request, actingAs);

    public Task<T> GetAsync<T>(string path, string? actingAs = null) =>
        SendAsync<T>(HttpMethod.Get, path, null, actingAs);

    public Task<HttpResponseMessage> GetRawAsync(string path, string? actingAs = null) =>
        SendRawAsync(HttpMethod.Get, path, null, actingAs);

    public Task<T> PutAsync<T>(string path, object request, string? actingAs = null) =>
        SendAsync<T>(HttpMethod.Put, path, request, actingAs);

    public Task<HttpResponseMessage> PutRawAsync(string path, object request, string? actingAs = null) =>
        SendRawAsync(HttpMethod.Put, path, request, actingAs);

    public Task<T> PatchAsync<T>(string path, object request, string? actingAs = null) =>
        SendAsync<T>(HttpMethod.Patch, path, request, actingAs);

    public Task<HttpResponseMessage> PatchRawAsync(string path, object request, string? actingAs = null) =>
        SendRawAsync(HttpMethod.Patch, path, request, actingAs);

    public Task<HttpResponseMessage> DeleteRawAsync(string path, string? actingAs = null) =>
        SendRawAsync(HttpMethod.Delete, path, null, actingAs);

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? request = null, string? actingAs = null)
    {
        var response = await SendRawAsync(method, path, request, actingAs);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"{method} {path} returned null body");
    }

    public async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method, string path, object? request = null, string? actingAs = null)
    {
        var message = new HttpRequestMessage(method, Relative(path));

        if (request != null)
            message.Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8, "application/json");

        ApplyAuth(message, actingAs);
        return await httpClient.SendAsync(message);
    }

    // Test steps use absolute-style paths ("/auth/login"); resolve them relative to the
    // gateway base address so its path segment (e.g. floci's /execute-api/{id}/{stage})
    // is preserved rather than discarded by absolute-path URI resolution.
    private static string Relative(string path) => path.TrimStart('/');

    private void ApplyAuth(HttpRequestMessage message, string? actingAs)
    {
        var user = actingAs ?? ActingAs;
        var token = users.GetToken(user);
        if (token != null)
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}
