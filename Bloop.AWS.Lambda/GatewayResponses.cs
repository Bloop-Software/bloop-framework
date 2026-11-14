using Amazon.Lambda.APIGatewayEvents;
using System.Net;
using System.Text.Json;

namespace Bloop.AWS.Lambda
{
    internal static class GatewayResponses
    {
        public const string JsonContentType = "application/json";

        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static APIGatewayHttpApiV2ProxyResponse Json(
            HttpStatusCode statusCode, object? body, IEnumerable<KeyValuePair<string, string>>? headers = null)
        {
            var responseHeaders = new Dictionary<string, string> { ["Content-Type"] = JsonContentType };
            if (headers != null)
                foreach (var header in headers)
                    responseHeaders[header.Key] = header.Value;

            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = (int)statusCode,
                Headers = responseHeaders,
                Body = Serialize(body),
            };
        }

        public static APIGatewayHttpApiV2ProxyResponse Error(HttpStatusCode statusCode, string detail) =>
            Json(statusCode, new { detail });

        public static string Serialize(object? value) =>
            value == null
                ? "null"
                : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
    }
}
