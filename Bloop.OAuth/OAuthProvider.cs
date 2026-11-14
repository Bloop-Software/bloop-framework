namespace Bloop.OAuth
{
    public sealed record OAuthProvider
    {
        public const string TenantIdPlaceholder = "{tenantid}";

        public required string Name { get; init; }

        public required string AuthorizationEndpoint { get; init; }

        public required string TokenEndpoint { get; init; }

        public required string JwksUri { get; init; }

        public required string ClientId { get; init; }

        public required string ClientSecret { get; init; }

        // An issuer may contain {tenantid}, which is replaced with the token's own tid claim.
        // Multi-tenant Microsoft sign-in issues tokens from a per-tenant issuer.
        public required IReadOnlyList<string> ValidIssuers { get; init; }

        public IReadOnlyDictionary<string, string> AuthorizeParameters { get; init; } =
            new Dictionary<string, string>();

        public static OAuthProvider Microsoft(
            string clientId,
            string clientSecret,
            string tenant = "organizations",
            string authorityBaseUrl = "https://login.microsoftonline.com")
        {
            var authority = authorityBaseUrl.TrimEnd('/');

            return new OAuthProvider
            {
                Name = "microsoft",
                AuthorizationEndpoint = $"{authority}/{tenant}/oauth2/v2.0/authorize",
                TokenEndpoint = $"{authority}/{tenant}/oauth2/v2.0/token",
                JwksUri = $"{authority}/{tenant}/discovery/v2.0/keys",
                ClientId = clientId,
                ClientSecret = clientSecret,
                ValidIssuers = [$"{authority}/{TenantIdPlaceholder}/v2.0"],
            };
        }

        public static OAuthProvider Google(string clientId, string clientSecret) => new()
        {
            Name = "google",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            JwksUri = "https://www.googleapis.com/oauth2/v3/certs",
            ClientId = clientId,
            ClientSecret = clientSecret,
            ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
        };
    }
}
