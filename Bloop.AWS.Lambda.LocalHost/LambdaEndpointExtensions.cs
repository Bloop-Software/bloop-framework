using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Bloop.AWS.Lambda.LocalHost
{
    public delegate Task<APIGatewayHttpApiV2ProxyResponse> LambdaFunction(GatewayEvent request, ILambdaContext context);

    // Hosts Lambda proxy handlers inside Kestrel, translating each HTTP request into the
    // HTTP API v2 event the handler receives in AWS. Used for local debugging and for
    // self-hosted deployments that run without Lambda.
    public static class LambdaEndpointExtensions
    {
        extension(IEndpointRouteBuilder endpoints)
        {
            public void MapLambda(string pathPrefix, string functionName, LambdaFunction function)
            {
                var prefix = "/" + pathPrefix.Trim('/');

                endpoints.Map(prefix, context => InvokeAsync(context, functionName, function));
                endpoints.Map(prefix + "/{**rest}", context => InvokeAsync(context, functionName, function));
            }
        }

        private static async Task InvokeAsync(HttpContext httpContext, string functionName, LambdaFunction function)
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(functionName);
            var lambdaContext = new LocalLambdaContext(functionName, httpContext.TraceIdentifier, logger);

            var response = await function(await ToGatewayEventAsync(httpContext), lambdaContext);

            httpContext.Response.StatusCode = response.StatusCode;

            if (response.Headers != null)
                foreach (var header in response.Headers)
                    httpContext.Response.Headers[header.Key] = header.Value;

            if (!string.IsNullOrEmpty(response.Body))
            {
                var body = response.IsBase64Encoded
                    ? Convert.FromBase64String(response.Body)
                    : Encoding.UTF8.GetBytes(response.Body);
                await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted);
            }
        }

        private static async Task<GatewayEvent> ToGatewayEventAsync(HttpContext httpContext)
        {
            var request = httpContext.Request;

            string? body = null;
            if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0)
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                body = await reader.ReadToEndAsync(httpContext.RequestAborted);
            }

            var path = request.PathBase.Add(request.Path).Value ?? "/";

            return new GatewayEvent
            {
                RawPath = path,
                RawQueryString = request.QueryString.HasValue ? request.QueryString.Value![1..] : "",
                Headers = request.Headers.ToDictionary(
                    h => h.Key.ToLowerInvariant(), h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                QueryStringParameters = request.Query.Count == 0
                    ? null
                    : request.Query.ToDictionary(q => q.Key, q => q.Value.ToString()),
                Body = string.IsNullOrEmpty(body) ? null : body,
                RouteKey = $"{request.Method} {path}",
                RequestContext = new GatewayRequestContext
                {
                    Http = new GatewayRequestHttp
                    {
                        Method = request.Method,
                        Path = path,
                        SourceIp = httpContext.Connection.RemoteIpAddress?.ToString(),
                    },
                },
            };
        }
    }
}
