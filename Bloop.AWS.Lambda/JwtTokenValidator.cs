using Bloop.WebApi;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace Bloop.AWS.Lambda
{
    public sealed record AuthenticatedToken(Guid UserId, Guid SessionId);

    public class JwtTokenValidator(string issuer, string audience, string privateKey)
    {
        private const string InvalidTokenMessage = "Invalid token";

        public static JwtTokenValidator FromEnvironment()
        {
            return new JwtTokenValidator(
                RequireEnv("JWT_ISSUER"),
                RequireEnv("JWT_AUDIENCE"),
                RequireEnv("JWT_PRIVATE_KEY"));
        }

        public AuthenticatedToken Validate(string token)
        {
            var parameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(privateKey)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };

            // Clear the inbound claim map so claims keep their raw JWT names (sub, sid).
            var handler = new JwtSecurityTokenHandler();
            handler.InboundClaimTypeMap.Clear();

            ClaimsPrincipal principal;
            try
            {
                principal = handler.ValidateToken(token, parameters, out _);
            }
            catch (Exception)
            {
                throw new WebApiException(HttpStatusCode.Unauthorized, InvalidTokenMessage);
            }

            if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
                || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sid)?.Value, out var sessionId))
            {
                throw new WebApiException(HttpStatusCode.Unauthorized, InvalidTokenMessage);
            }

            return new AuthenticatedToken(userId, sessionId);
        }

        private static string RequireEnv(string name) =>
            Environment.GetEnvironmentVariable(name)
                ?? throw new Exception($"{name} environment variable not set");
    }
}
