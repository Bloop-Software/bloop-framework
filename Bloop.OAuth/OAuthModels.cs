using System.Net;

namespace Bloop.OAuth
{
    public sealed record AuthorizeRequest(
        string RedirectUri,
        IReadOnlyList<string> Scopes,
        string State,
        string? CodeChallenge = null,
        string? Nonce = null,
        IReadOnlyDictionary<string, string>? ExtraParameters = null);

    public sealed record OAuthTokens(
        string AccessToken,
        string? RefreshToken,
        string? IdToken,
        DateTimeOffset? ExpiresAt,
        string? Scope);

    public sealed record IdTokenClaims(
        string Issuer,
        string Subject,
        string? TenantId,
        string? ObjectId,
        string? Email,
        bool EmailVerified,
        string? Name,
        string? GivenName,
        string? FamilyName,
        string? HostedDomain);

    public class OAuthException(string error, string? description = null, HttpStatusCode? statusCode = null)
        : Exception(description == null ? error : $"{error}: {description}")
    {
        public const string InvalidGrant = "invalid_grant";
        public const string InvalidIdToken = "invalid_id_token";

        public string Error { get; } = error;

        public string? Description { get; } = description;

        public HttpStatusCode? StatusCode { get; } = statusCode;
    }
}
