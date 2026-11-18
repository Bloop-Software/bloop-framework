using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Bloop.WebApi;

namespace Bloop.AWS.Lambda
{
    public abstract class Handler<TRequest, TResponse> : LambdaHost
    {
        protected GatewayEvent? GatewayRequest { get; private set; }

        // Verifies the bearer token's signature, issuer, audience and lifetime, and returns
        // its identity claims. Does not consult session state; use GetAuthenticatedUserIdAsync
        // in request handlers so a registered ISessionValidator is also enforced.
        protected AuthenticatedToken GetAuthenticatedToken() =>
            BearerAuthentication.Authenticate(null, GatewayRequest);

        protected async Task<Guid> GetAuthenticatedUserIdAsync(IServiceProvider services) =>
            (await BearerAuthentication.AuthenticateSessionAsync(services, GatewayRequest)).UserId;

        protected abstract Task<WebApiResponse<TResponse>> HandleRequestAsync(
            IServiceProvider services, TRequest? request);

        public Task<APIGatewayHttpApiV2ProxyResponse> Handle(GatewayEvent request, ILambdaContext context)
        {
            GatewayRequest = request;

            return ExecuteAsync(request, context, async services =>
            {
                var payload = DeserializeBody<TRequest>(request);
                var result = await HandleRequestAsync(services, payload);

                return GatewayResponses.Json(result.StatusCode, result.Response, result.Headers);
            });
        }
    }
}
