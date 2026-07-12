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

    public async Task<T> PostAsync<T>(string path, object request, string? actingAs = null)
    {
        var response = await PostRawAsync(path, request, actingAs);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"POST {path} returned null body");
    }

    public async Task<HttpResponseMessage> PostRawAsync(string path, object request, string? actingAs = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Relative(path))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8, "application/json"),
        };

        ApplyAuth(message, actingAs);
        return await httpClient.SendAsync(message);
    }

    public async Task<T> GetAsync<T>(string path, string? actingAs = null)
    {
        var response = await GetRawAsync(path, actingAs);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"GET {path} returned null body");
    }

    public async Task<HttpResponseMessage> GetRawAsync(string path, string? actingAs = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, Relative(path));
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
