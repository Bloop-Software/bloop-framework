using Bloop.WebApi;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Bloop.AWS.Lambda
{
    internal static class BearerAuthentication
    {
        private const string Scheme = "Bearer ";

        private static JwtTokenValidator? _environmentValidator;

        public static AuthenticatedToken Authenticate(IServiceProvider? services, GatewayEvent? request)
        {
            var header = request?.GetHeader("Authorization");

            if (string.IsNullOrEmpty(header) || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
                throw new WebApiException(HttpStatusCode.Unauthorized, "Authentication required");

            var validator = services?.GetService<JwtTokenValidator>()
                ?? (_environmentValidator ??= JwtTokenValidator.FromEnvironment());

            return validator.Validate(header[Scheme.Length..]);
        }

        public static async Task<AuthenticatedToken> AuthenticateSessionAsync(
            IServiceProvider services, GatewayEvent? request)
        {
            var token = Authenticate(services, request);

            var sessionValidator = services.GetService<ISessionValidator>();
            if (sessionValidator != null)
                await sessionValidator.ValidateSessionAsync(token.UserId, token.SessionId);

            return token;
        }
    }
}
