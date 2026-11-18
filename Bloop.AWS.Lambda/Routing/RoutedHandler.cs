using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Bloop.WebApi;
using System.Net;

namespace Bloop.AWS.Lambda.Routing
{
    // Serves every endpoint of one feature from a single Lambda function. The gateway
    // forwards the feature's whole path prefix here and the handler routes internally.
    public abstract class RoutedHandler : LambdaHost
    {
        private RouteBuilder? _routes;

        protected abstract void MapRoutes(RouteBuilder routes);

        public Task<APIGatewayHttpApiV2ProxyResponse> Handle(GatewayEvent request, ILambdaContext context) =>
            ExecuteAsync(request, context, async services =>
            {
                var method = request.ResolvedMethod
                    ?? throw new WebApiException("Request method is missing");

                var match = GetRoutes().Match(method, request.ResolvedPath ?? "/");
                if (match.Route == null)
                {
                    throw match.PathMatched
                        ? new WebApiException(HttpStatusCode.MethodNotAllowed, "Method not allowed")
                        : new WebApiException(HttpStatusCode.NotFound, "Not found");
                }

                var result = await match.Route.Handler(new ApiRequest(services, request, method, match.Values));
                return result.ToGatewayResponse();
            });

        private RouteBuilder GetRoutes()
        {
            if (_routes == null)
            {
                var routes = new RouteBuilder();
                MapRoutes(routes);
                _routes = routes;
            }

            return _routes;
        }
    }
}
