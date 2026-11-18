using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;

namespace Bloop.OAuth
{
    // Validates an OpenID Connect id_token against the provider's published signing keys.
    public class IdTokenValidator(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        private static readonly TimeSpan KeyCacheLifetime = TimeSpan.FromHours(6);

        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
        private readonly ConcurrentDictionary<string, (JsonWebKeySet Keys, DateTimeOffset FetchedAt)> _keyCache = new();

        public virtual async Task<IdTokenClaims> ValidateAsync(
            OAuthProvider provider,
            string idToken,
            string? expectedNonce = null,
            CancellationToken cancellationToken = default)
        {
            var handler = new JsonWebTokenHandler { MapInboundClaims = false };

            JsonWebToken unvalidated;
            try
            {
                unvalidated = handler.ReadJsonWebToken(idToken);
            }
            catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
            {
                throw Invalid("The id_token is malformed");
            }

            var tenantId = GetClaim(unvalidated, "tid");
            var issuers = provider.ValidIssuers
                .Where(i => tenantId != null || !i.Contains(OAuthProvider.TenantIdPlaceholder))
                .Select(i => i.Replace(OAuthProvider.TenantIdPlaceholder, tenantId))
                .ToList();

            var result = await ValidateWithKeysAsync(handler, provider, idToken, issuers, forceRefresh: false, cancellationToken);

            // Providers rotate signing keys; an unknown key id is worth one fresh fetch.
            if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
                result = await ValidateWithKeysAsync(handler, provider, idToken, issuers, forceRefresh: true, cancellationToken);

            if (!result.IsValid)
                throw Invalid(result.Exception?.GetType().Name ?? "Validation failed");

            var token = (JsonWebToken)result.SecurityToken;

            if (expectedNonce != null && GetClaim(token, "nonce") != expectedNonce)
                throw Invalid("Nonce mismatch");

            var subject = GetClaim(token, "sub");
            if (string.IsNullOrEmpty(subject))
                throw Invalid("The id_token has no subject");

            return new IdTokenClaims(
                token.Issuer,
                subject,
                tenantId,
                GetClaim(token, "oid"),
                GetClaim(token, "email") ?? GetClaim(token, "preferred_username"),
                IsTrue(token, "email_verified"),
                GetClaim(token, "name"),
                GetClaim(token, "given_name"),
                GetClaim(token, "family_name"),
                GetClaim(token, "hd"));
        }

        private async Task<TokenValidationResult> ValidateWithKeysAsync(
            JsonWebTokenHandler handler,
            OAuthProvider provider,
            string idToken,
            IReadOnlyList<string> issuers,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var keys = await GetKeysAsync(provider.JwksUri, forceRefresh, cancellationToken);

            var parameters = new TokenValidationParameters
            {
                ValidIssuers = issuers,
                ValidAudience = provider.ClientId,
                IssuerSigningKeys = keys.GetSigningKeys(),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            return await handler.ValidateTokenAsync(idToken, parameters);
        }

        private async Task<JsonWebKeySet> GetKeysAsync(string jwksUri, bool forceRefresh, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow();

            if (!forceRefresh
                && _keyCache.TryGetValue(jwksUri, out var cached)
                && now - cached.FetchedAt < KeyCacheLifetime)
                return cached.Keys;

            var json = await httpClient.GetStringAsync(jwksUri, cancellationToken);
            var keys = new JsonWebKeySet(json);
            _keyCache[jwksUri] = (keys, now);
            return keys;
        }

        private static string? GetClaim(JsonWebToken token, string name) =>
            token.TryGetPayloadValue<string>(name, out var value) ? value : null;

        // Google documents email_verified as a boolean but has been seen sending the string "true".
        private static bool IsTrue(JsonWebToken token, string name)
        {
            if (token.TryGetPayloadValue<bool>(name, out var flag))
                return flag;

            return token.TryGetPayloadValue<string>(name, out var text)
                && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static OAuthException Invalid(string description) =>
            new(OAuthException.InvalidIdToken, description);
    }
}
