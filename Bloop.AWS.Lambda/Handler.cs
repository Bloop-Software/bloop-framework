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
using System.Text;
using System.Text.Json;

namespace Bloop.AWS.Lambda
{
    public abstract class Handler<TRequest, TResponse>
    {
        // Web defaults: camelCase property names on the wire, case-insensitive binding.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private static ServiceProvider? _services;
        private static readonly object _lock = new();

        private static JwtTokenValidator? _tokenValidator;

        protected GatewayEvent? GatewayRequest { get; private set; }

        // Verifies the bearer token's signature, issuer, audience and lifetime, and returns
        // its identity claims. Does not consult session state; use GetAuthenticatedUserIdAsync
        // in request handlers so a registered ISessionValidator is also enforced.
        protected AuthenticatedToken GetAuthenticatedToken()
        {
            var authHeader = GatewayRequest?.Headers
                ?.FirstOrDefault(h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)).Value;

            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                throw new WebApiException(HttpStatusCode.Unauthorized, "Authentication required");

            _tokenValidator ??= JwtTokenValidator.FromEnvironment();
            return _tokenValidator.Validate(authHeader["Bearer ".Length..]);
        }

        protected async Task<Guid> GetAuthenticatedUserIdAsync(IServiceProvider services)
        {
            var token = GetAuthenticatedToken();

            var sessionValidator = services.GetService<ISessionValidator>();
            if (sessionValidator != null)
                await sessionValidator.ValidateSessionAsync(token.UserId, token.SessionId);

            return token.UserId;
        }

        protected virtual void ConfigureServices(IServiceCollection services) { }

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

        protected abstract Task<WebApiResponse<TResponse>> HandleRequestAsync(
            IServiceProvider services, TRequest? request);

        public async Task<APIGatewayHttpApiV2ProxyResponse> Handle(GatewayEvent request, ILambdaContext context)
        {
            GatewayRequest = request;

            if (_services == null)
            {
                lock (_lock)
                {
                    _services ??= BuildServiceProvider();
                }
            }

            using var scope = _services.CreateScope();

            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress =
                request.ResolvedSourceIp != null
                    && IPAddress.TryParse(request.ResolvedSourceIp, out var ip)
                    ? ip : IPAddress.Loopback;
            if (request.Headers?.TryGetValue("User-Agent", out var userAgent) == true)
                httpContext.Request.Headers["User-Agent"] = userAgent;
            accessor.HttpContext = httpContext;

            try
            {
                TRequest? payload = default;
                if (!string.IsNullOrEmpty(request.Body))
                    payload = JsonSerializer.Deserialize<TRequest>(request.Body, JsonOptions);

                var result = await HandleRequestAsync(scope.ServiceProvider, payload);

                var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
                foreach (var header in result.Headers)
                    headers[header.Key] = header.Value;

                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = (int)result.StatusCode,
                    Headers = headers,
                    Body = JsonSerializer.Serialize(result.Response, JsonOptions),
                };
            }
            catch (JsonException)
            {
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 400,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(new { detail = "Invalid request body" })
                };
            }
            catch (WebApiException ex)
            {
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = (int)ex.StatusCode,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(new { detail = ex.Message })
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Unhandled error for request {context.TraceId}: {ex.Message}\r\n at {ex.StackTrace}");

                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 500,
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    Body = JsonSerializer.Serialize(new { detail = "An error occurred" })
                };
            }
        }
    }
}
