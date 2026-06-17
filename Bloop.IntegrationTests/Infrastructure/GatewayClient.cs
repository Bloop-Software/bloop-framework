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
        var message = new HttpRequestMessage(HttpMethod.Post, path)
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
        var message = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(message, actingAs);
        return await httpClient.SendAsync(message);
    }

    private void ApplyAuth(HttpRequestMessage message, string? actingAs)
    {
        var user = actingAs ?? ActingAs;
        var token = users.GetToken(user);
        if (token != null)
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}
