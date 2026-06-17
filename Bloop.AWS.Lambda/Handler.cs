using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Bloop.WebApi;
using Destructurama;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Net;
using System.Text.Json;

namespace Bloop.AWS.Lambda
{
    public abstract class Handler<TRequest, TResponse>
    {
        private static ServiceProvider? _services;
        private static readonly object _lock = new();

        protected APIGatewayProxyRequest? GatewayRequest { get; private set; }

        protected virtual void ConfigureServices(IServiceCollection services) { }

        protected abstract Task<WebApiResponse<TResponse>> HandleRequestAsync(
            IServiceProvider services, TRequest? request);

        public async Task<APIGatewayProxyResponse> Handle(APIGatewayProxyRequest request, ILambdaContext context)
        {
            GatewayRequest = request;

            if (_services == null)
            {
                lock (_lock)
                {
                    if (_services == null)
                    {
                        var sc = new ServiceCollection();
                        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
                        var useOtlp = !string.IsNullOrEmpty(otlpEndpoint);
                        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "bloop";

                        var loggerConfig = new LoggerConfiguration()
                            .MinimumLevel.Information()
                            .Enrich.FromLogContext()
                            .Destructure.UsingAttributes();

                        if (useOtlp)
                            loggerConfig.WriteTo.OpenTelemetry(o =>
                            {
                                o.Endpoint = otlpEndpoint!;
                                o.ResourceAttributes = new Dictionary<string, object>
                                {
                                    ["service.name"] = serviceName,
                                };
                                var headers = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS");
                                if (!string.IsNullOrEmpty(headers))
                                    foreach (var pair in headers.Split(','))
                                    {
                                        var parts = pair.Split('=', 2);
                                        if (parts.Length == 2)
                                            o.Headers[parts[0].Trim()] = parts[1].Trim();
                                    }
                            });
                        else
                            loggerConfig.WriteTo.Console();

                        sc.AddLogging(builder =>
                        {
                            builder.ClearProviders();
                            builder.AddSerilog(loggerConfig.CreateLogger(), dispose: true);
                        });

                        sc.AddOpenTelemetry()
                            .ConfigureResource(r => r.AddService(serviceName))
                            .WithTracing(tracing =>
                            {
                                tracing.AddHttpClientInstrumentation();
                                if (useOtlp)
                                    tracing.AddOtlpExporter();
                            })
                            .WithMetrics(metrics =>
                            {
                                metrics.AddHttpClientInstrumentation();
                                if (useOtlp)
                                    metrics.AddOtlpExporter();
                            });

                        sc.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                        ConfigureServices(sc);
                        _services = sc.BuildServiceProvider();
                    }
                }
            }

            using var scope = _services.CreateScope();

            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = new DefaultHttpContext();
            if (request.RequestContext?.Identity?.SourceIp != null)
                httpContext.Connection.RemoteIpAddress =
                    IPAddress.TryParse(request.RequestContext.Identity.SourceIp, out var ip)
                        ? ip : IPAddress.Loopback;
            if (request.Headers?.TryGetValue("User-Agent", out var userAgent) == true)
                httpContext.Request.Headers["User-Agent"] = userAgent;
            accessor.HttpContext = httpContext;

            try
            {
                var payload = JsonSerializer.Deserialize<TRequest>(request.Body);
                var result = await HandleRequestAsync(scope.ServiceProvider, payload);

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)result.StatusCode,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(result.Response),
                };
            }
            catch (JsonException)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(new { detail = "Invalid request body" })
                };
            }
            catch (WebApiException ex)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)ex.StatusCode,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(new { detail = ex.Message })
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Unhandled error for request {context.TraceId}: {ex.Message}\r\n at {ex.StackTrace}");

                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(new { detail = "An error occurred" })
                };
            }
        }
    }
}
