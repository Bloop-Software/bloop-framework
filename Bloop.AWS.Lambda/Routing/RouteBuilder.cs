namespace Bloop.AWS.Lambda.Routing
{
    internal sealed record Route(string Method, RouteTemplate Template, Func<ApiRequest, Task<ApiResult>> Handler);

    internal sealed record RouteMatch(Route? Route, IReadOnlyDictionary<string, string> Values, bool PathMatched)
    {
        public static readonly RouteMatch NotFound =
            new(null, new Dictionary<string, string>(), PathMatched: false);
    }

    public sealed class RouteBuilder
    {
        private readonly List<Route> _routes = [];

        public RouteBuilder Map(string method, string template, Func<ApiRequest, Task<ApiResult>> handler)
        {
            _routes.Add(new Route(method.ToUpperInvariant(), RouteTemplate.Parse(template), handler));
            return this;
        }

        public RouteBuilder Get(string template, Func<ApiRequest, Task<ApiResult>> handler) =>
            Map("GET", template, handler);

        public RouteBuilder Post(string template, Func<ApiRequest, Task<ApiResult>> handler) =>
            Map("POST", template, handler);

        public RouteBuilder Put(string template, Func<ApiRequest, Task<ApiResult>> handler) =>
            Map("PUT", template, handler);

        public RouteBuilder Patch(string template, Func<ApiRequest, Task<ApiResult>> handler) =>
            Map("PATCH", template, handler);

        public RouteBuilder Delete(string template, Func<ApiRequest, Task<ApiResult>> handler) =>
            Map("DELETE", template, handler);

        public RouteBuilder Post<TBody>(string template, Func<ApiRequest, TBody, Task<ApiResult>> handler) =>
            Map("POST", template, request => handler(request, request.ReadBody<TBody>()));

        public RouteBuilder Put<TBody>(string template, Func<ApiRequest, TBody, Task<ApiResult>> handler) =>
            Map("PUT", template, request => handler(request, request.ReadBody<TBody>()));

        public RouteBuilder Patch<TBody>(string template, Func<ApiRequest, TBody, Task<ApiResult>> handler) =>
            Map("PATCH", template, request => handler(request, request.ReadBody<TBody>()));

        internal RouteMatch Match(string method, string path)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Offset 0 is an exact match. Larger offsets skip leading segments an emulator adds
            // in front of the route (for example /execute-api/{apiId}/{stage}).
            for (var offset = 0; offset < segments.Length; offset++)
            {
                Route? best = null;
                IReadOnlyDictionary<string, string>? bestValues = null;
                var pathMatched = false;

                foreach (var route in _routes)
                {
                    if (!route.Template.TryMatch(segments, offset, out var values))
                        continue;

                    pathMatched = true;

                    if (!route.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (best == null || route.Template.LiteralCount > best.Template.LiteralCount)
                    {
                        best = route;
                        bestValues = values;
                    }
                }

                if (best != null)
                    return new RouteMatch(best, bestValues!, PathMatched: true);

                if (pathMatched)
                    return RouteMatch.NotFound with { PathMatched = true };
            }

            return RouteMatch.NotFound;
        }
    }
}
