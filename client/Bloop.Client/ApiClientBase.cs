using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bloop.Client
{
    // Where the API lives. Held separately from HttpClient so the address can be changed
    // at run time, for example from a settings screen.
    public sealed class ApiEndpoint
    {
        private Uri? _baseAddress;

        public Uri BaseAddress
        {
            get => _baseAddress ?? throw new InvalidOperationException("The server address has not been set");
            set => _baseAddress = new Uri(value.AbsoluteUri.TrimEnd('/') + "/");
        }

        public bool IsConfigured => _baseAddress != null;

        public Uri Resolve(string path) => new(BaseAddress, path.TrimStart('/'));
    }

    public class ApiException(HttpStatusCode statusCode, string detail) : Exception(detail)
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    // Typed JSON client for APIs that report errors as { "detail": "..." }. Callers pass
    // source-generated type metadata so the client stays usable under trimming and AOT.
    public abstract class ApiClientBase(HttpClient httpClient, ApiEndpoint endpoint)
    {
        protected Task<TResponse> GetAsync<TResponse>(
            string path, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken = default) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Get, endpoint.Resolve(path)), responseType, cancellationToken);

        protected Task<TResponse> SendAsync<TRequest, TResponse>(
            HttpMethod method,
            string path,
            TRequest body,
            JsonTypeInfo<TRequest> requestType,
            JsonTypeInfo<TResponse> responseType,
            CancellationToken cancellationToken = default) =>
            SendAsync(CreateRequest(method, path, body, requestType), responseType, cancellationToken);

        protected async Task SendAsync<TRequest>(
            HttpMethod method,
            string path,
            TRequest body,
            JsonTypeInfo<TRequest> requestType,
            CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(method, path, body, requestType);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }

        protected async Task SendAsync(HttpMethod method, string path, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(method, endpoint.Resolve(path));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }

        protected Task<TResponse> SendAsync<TResponse>(
            HttpMethod method, string path, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken = default) =>
            SendAsync(new HttpRequestMessage(method, endpoint.Resolve(path)), responseType, cancellationToken);

        private HttpRequestMessage CreateRequest<TRequest>(
            HttpMethod method, string path, TRequest body, JsonTypeInfo<TRequest> requestType) =>
            new(method, endpoint.Resolve(path)) { Content = JsonContent.Create(body, requestType) };

        private async Task<TResponse> SendAsync<TResponse>(
            HttpRequestMessage request, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken)
        {
            using (request)
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                await EnsureSuccessAsync(response, cancellationToken);

                return await response.Content.ReadFromJsonAsync(responseType, cancellationToken)
                    ?? throw new ApiException(response.StatusCode, "The server returned an empty response");
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
                return;

            var detail = $"The server returned {(int)response.StatusCode}";

            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("detail", out var value)
                    && value.ValueKind == JsonValueKind.String)
                    detail = value.GetString() ?? detail;
            }
            catch (JsonException)
            {
            }

            throw new ApiException(response.StatusCode, detail);
        }
    }
}
