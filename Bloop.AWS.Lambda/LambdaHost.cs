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
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Bloop.AWS.Lambda
{
    public abstract class LambdaHost
    {
        // Keyed by concrete handler type so several handlers can share one process,
        // as they do in tests and in the local host.
        private static readonly ConcurrentDictionary<Type, Lazy<ServiceProvider>> Providers = new();

        protected virtual void ConfigureServices(IServiceCollection services) { }

        protected IServiceProvider RootServices =>
            Providers.GetOrAdd(GetType(), _ => new Lazy<ServiceProvider>(BuildServiceProvider)).Value;

        protected IServiceScope CreateScope(GatewayEvent? request = null)
        {
            var scope = RootServices.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

            if (request == null)
            {
                accessor.HttpContext = null;
                return scope;
            }

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress =
                IPAddress.TryParse(request.ResolvedSourceIp, out var ip) ? ip : IPAddress.Loopback;

            var userAgent = request.GetHeader("User-Agent");
            if (userAgent != null)
                httpContext.Request.Headers.UserAgent = userAgent;

            accessor.HttpContext = httpContext;
            return scope;
        }

        protected async Task<APIGatewayHttpApiV2ProxyResponse> ExecuteAsync(
            GatewayEvent request,
            ILambdaContext context,
            Func<IServiceProvider, Task<APIGatewayHttpApiV2ProxyResponse>> action)
        {
            using var scope = CreateScope(request);

            try
            {
                return await action(scope.ServiceProvider);
            }
            catch (WebApiException ex)
            {
                return GatewayResponses.Error(ex.StatusCode, ex.Message);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Unhandled error for request {context.TraceId}: {ex.Message}\r\n at {ex.StackTrace}");

                return GatewayResponses.Error(HttpStatusCode.InternalServerError, "An error occurred");
            }
        }

        protected static T? DeserializeBody<T>(GatewayEvent request)
        {
            var body = request.ResolvedBody;
            if (string.IsNullOrEmpty(body))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(body, GatewayResponses.JsonOptions);
            }
            catch (JsonException)
            {
                throw new WebApiException("Invalid request body");
            }
        }

        private ServiceProvider BuildServiceProvider()
        {
            var sc = new ServiceCollection();
            AddLoggingAndTelemetry(sc);
            sc.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            ConfigureServices(sc);
            return sc.BuildServiceProvider();
        }

        private static void AddLoggingAndTelemetry(IServiceCollection sc)
        {
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
        }
    }
}
