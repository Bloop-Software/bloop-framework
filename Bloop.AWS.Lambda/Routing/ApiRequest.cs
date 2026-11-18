using Bloop.WebApi;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;

namespace Bloop.AWS.Lambda.Routing
{
    public sealed class ApiRequest
    {
        private IReadOnlyDictionary<string, string>? _query;

        internal ApiRequest(
            IServiceProvider services,
            GatewayEvent gateway,
            string method,
            IReadOnlyDictionary<string, string> routeValues)
        {
            Services = services;
            Gateway = gateway;
            Method = method;
            RouteValues = routeValues;
        }

        public IServiceProvider Services { get; }

        public GatewayEvent Gateway { get; }

        public string Method { get; }

        public string Path => Gateway.ResolvedPath ?? "/";

        public IReadOnlyDictionary<string, string> RouteValues { get; }

        public IReadOnlyDictionary<string, string> Query => _query ??= ReadQuery();

        public string Route(string name) =>
            RouteValues.TryGetValue(name, out var value)
                ? value
                : throw new InvalidOperationException($"Route parameter '{name}' is not part of the matched route");

        public Guid RouteGuid(string name) =>
            Guid.TryParse(Route(name), out var value)
                ? value
                : throw new WebApiException($"Route parameter '{name}' is invalid");

        public string? Header(string name) => Gateway.GetHeader(name);

        public T BindQuery<T>() => QueryBinder.Bind<T>(Query);

        public T ReadBody<T>()
        {
            var body = Gateway.ResolvedBody;
            if (string.IsNullOrWhiteSpace(body))
                throw new WebApiException("Request body is required");

            try
            {
                return JsonSerializer.Deserialize<T>(body, GatewayResponses.JsonOptions)
                    ?? throw new WebApiException("Request body is required");
            }
            catch (JsonException)
            {
                throw new WebApiException("Invalid request body");
            }
        }

        public Task<AuthenticatedToken> RequireSessionAsync() =>
            BearerAuthentication.AuthenticateSessionAsync(Services, Gateway);

        public async Task<Guid> RequireUserIdAsync() =>
            (await RequireSessionAsync()).UserId;

        private IReadOnlyDictionary<string, string> ReadQuery()
        {
            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (Gateway.QueryStringParameters != null)
            {
                foreach (var pair in Gateway.QueryStringParameters)
                    query[pair.Key] = pair.Value;
            }
            else if (!string.IsNullOrEmpty(Gateway.RawQueryString))
            {
                foreach (var pair in QueryHelpers.ParseQuery(Gateway.RawQueryString))
                    query[pair.Key] = pair.Value.ToString();
            }

            return query;
        }
    }
}
