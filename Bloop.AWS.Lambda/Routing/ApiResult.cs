using Amazon.Lambda.APIGatewayEvents;
using System.Net;

namespace Bloop.AWS.Lambda.Routing
{
    public sealed class ApiResult
    {
        private ApiResult(HttpStatusCode statusCode, string? body, string? contentType)
        {
            StatusCode = statusCode;
            Body = body;
            ContentType = contentType;
        }

        public HttpStatusCode StatusCode { get; }

        public string? Body { get; }

        public string? ContentType { get; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static ApiResult Ok(object value) => Json(HttpStatusCode.OK, value);

        public static ApiResult Created(object value) => Json(HttpStatusCode.Created, value);

        public static ApiResult NoContent() => new(HttpStatusCode.NoContent, null, null);

        public static ApiResult Json(HttpStatusCode statusCode, object? value) =>
            new(statusCode, GatewayResponses.Serialize(value), GatewayResponses.JsonContentType);

        public static ApiResult Redirect(string location) =>
            new ApiResult(HttpStatusCode.Found, null, null).WithHeader("Location", location);

        public static ApiResult Content(HttpStatusCode statusCode, string contentType, string body) =>
            new(statusCode, body, contentType);

        public ApiResult WithHeader(string name, string value)
        {
            Headers[name] = value;
            return this;
        }

        internal APIGatewayHttpApiV2ProxyResponse ToGatewayResponse()
        {
            var headers = new Dictionary<string, string>(Headers, StringComparer.OrdinalIgnoreCase);
            if (ContentType != null)
                headers["Content-Type"] = ContentType;

            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = (int)StatusCode,
                Headers = headers,
                Body = Body,
            };
        }
    }
}
