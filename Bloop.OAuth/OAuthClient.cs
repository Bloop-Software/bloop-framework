using Bloop.Security;
using System.Text;
using System.Text.Json;

namespace Bloop.OAuth
{
    // Confidential-client side of the OAuth 2.0 authorization code flow.
    public class OAuthClient(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        public static string BuildAuthorizeUrl(OAuthProvider provider, AuthorizeRequest request)
        {
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("client_id", provider.ClientId),
                new("response_type", "code"),
                new("redirect_uri", request.RedirectUri),
                new("scope", string.Join(' ', request.Scopes)),
                new("state", request.State),
            };

            if (request.CodeChallenge != null)
            {
                parameters.Add(new("code_challenge", request.CodeChallenge));
                parameters.Add(new("code_challenge_method", Pkce.Method));
            }

            if (request.Nonce != null)
                parameters.Add(new("nonce", request.Nonce));

            parameters.AddRange(provider.AuthorizeParameters);
            if (request.ExtraParameters != null)
                parameters.AddRange(request.ExtraParameters);

            var query = new StringBuilder();
            foreach (var parameter in parameters)
            {
                query.Append(query.Length == 0 ? '?' : '&')
                    .Append(Uri.EscapeDataString(parameter.Key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(parameter.Value));
            }

            return provider.AuthorizationEndpoint + query;
        }

        public virtual Task<OAuthTokens> ExchangeCodeAsync(
            OAuthProvider provider,
            string code,
            string redirectUri,
            string? codeVerifier = null,
            CancellationToken cancellationToken = default)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            };

            if (codeVerifier != null)
                form["code_verifier"] = codeVerifier;

            return RequestTokensAsync(provider, form, cancellationToken);
        }

        public virtual Task<OAuthTokens> RefreshAsync(
            OAuthProvider provider,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            };

            return RequestTokensAsync(provider, form, cancellationToken);
        }

        private async Task<OAuthTokens> RequestTokensAsync(
            OAuthProvider provider, Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            form["client_id"] = provider.ClientId;
            form["client_secret"] = provider.ClientSecret;

            using var response = await httpClient.PostAsync(
                provider.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonElement json;
            try
            {
                using var document = JsonDocument.Parse(body);
                json = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new OAuthException("invalid_response",
                    $"Token endpoint returned {(int)response.StatusCode} with a non-JSON body", response.StatusCode);
            }

            var error = GetString(json, "error");
            if (error != null || !response.IsSuccessStatusCode)
                throw new OAuthException(
                    error ?? "token_request_failed", GetString(json, "error_description"), response.StatusCode);

            var accessToken = GetString(json, "access_token")
                ?? throw new OAuthException("invalid_response", "Token response has no access_token", response.StatusCode);

            DateTimeOffset? expiresAt = null;
            if (json.TryGetProperty("expires_in", out var expiresIn) && TryGetSeconds(expiresIn, out var seconds))
                expiresAt = _timeProvider.GetUtcNow().AddSeconds(seconds);

            return new OAuthTokens(
                accessToken,
                GetString(json, "refresh_token"),
                GetString(json, "id_token"),
                expiresAt,
                GetString(json, "scope"));
        }

        private static string? GetString(JsonElement json, string name) =>
            json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        // Some providers send expires_in as a string rather than a number.
        private static bool TryGetSeconds(JsonElement value, out double seconds)
        {
            seconds = 0;
            return value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetDouble(out seconds),
                JsonValueKind.String => double.TryParse(
                    value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out seconds),
                _ => false,
            };
        }
    }
}
