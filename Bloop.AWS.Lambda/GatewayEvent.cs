using System.Text;

namespace Bloop.AWS.Lambda
{
    // API Gateway proxy event tolerant of both the v1 (REST) and v2 (HTTP API) payload
    // shapes, since emulators do not always match the configured payload format version.
    public class GatewayEvent
    {
        public string? Body { get; set; }

        public bool IsBase64Encoded { get; set; }

        public Dictionary<string, string>? Headers { get; set; }

        public Dictionary<string, string>? PathParameters { get; set; }

        public Dictionary<string, string>? QueryStringParameters { get; set; }

        public string? RawQueryString { get; set; }

        public string? RawPath { get; set; }

        public string? Path { get; set; }

        public string? HttpMethod { get; set; }

        public string? RouteKey { get; set; }

        public GatewayRequestContext? RequestContext { get; set; }

        public string? ResolvedPath =>
            RawPath ?? Path ?? RequestContext?.Http?.Path;

        public string? ResolvedMethod =>
            RequestContext?.Http?.Method ?? HttpMethod;

        public string? ResolvedSourceIp =>
            RequestContext?.Http?.SourceIp ?? RequestContext?.Identity?.SourceIp;

        public string? ResolvedBody =>
            Body != null && IsBase64Encoded
                ? Encoding.UTF8.GetString(Convert.FromBase64String(Body))
                : Body;

        // HTTP API v2 lower-cases header names while v1 and emulators preserve the caller's casing.
        public string? GetHeader(string name)
        {
            if (Headers == null)
                return null;

            foreach (var header in Headers)
            {
                if (header.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return header.Value;
            }

            return null;
        }
    }

    public class GatewayRequestContext
    {
        public GatewayRequestIdentity? Identity { get; set; }

        public GatewayRequestHttp? Http { get; set; }
    }

    public class GatewayRequestIdentity
    {
        public string? SourceIp { get; set; }
    }

    public class GatewayRequestHttp
    {
        public string? Method { get; set; }

        public string? Path { get; set; }

        public string? SourceIp { get; set; }
    }
}
