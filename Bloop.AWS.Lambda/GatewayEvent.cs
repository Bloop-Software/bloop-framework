namespace Bloop.AWS.Lambda
{
    // API Gateway proxy event tolerant of both the v1 (REST) and v2 (HTTP API) payload
    // shapes, since emulators do not always match the configured payload format version.
    public class GatewayEvent
    {
        public string? Body { get; set; }

        public Dictionary<string, string>? Headers { get; set; }

        public Dictionary<string, string>? PathParameters { get; set; }

        public Dictionary<string, string>? QueryStringParameters { get; set; }

        public string? RawPath { get; set; }

        public string? Path { get; set; }

        public GatewayRequestContext? RequestContext { get; set; }

        public string? ResolvedPath =>
            RawPath ?? Path ?? RequestContext?.Http?.Path;

        public string? ResolvedSourceIp =>
            RequestContext?.Http?.SourceIp ?? RequestContext?.Identity?.SourceIp;
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
