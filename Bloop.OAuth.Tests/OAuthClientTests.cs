using System.Net;
using System.Web;
using Xunit;

namespace Bloop.OAuth.Tests;

public class OAuthClientTests
{
    private static readonly OAuthProvider Provider = OAuthProvider.Google("client-1", "secret-1");

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Dictionary<string, string> ParseForm(string? body)
    {
        var parsed = HttpUtility.ParseQueryString(body ?? "");
        return parsed.AllKeys.ToDictionary(k => k!, k => parsed[k]!);
    }

    [Fact]
    public void BuildAuthorizeUrl_IncludesPkceNonceAndExtras_Escaped()
    {
        var url = OAuthClient.BuildAuthorizeUrl(Provider, new AuthorizeRequest(
            "https://api.example.com/auth/cb?x=1",
            ["openid", "email"],
            "state value",
            CodeChallenge: "challenge",
            Nonce: "nonce-1",
            ExtraParameters: new Dictionary<string, string> { ["access_type"] = "offline", ["prompt"] = "consent" }));

        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("client-1", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("https://api.example.com/auth/cb?x=1", query["redirect_uri"]);
        Assert.Equal("openid email", query["scope"]);
        Assert.Equal("state value", query["state"]);
        Assert.Equal("challenge", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("nonce-1", query["nonce"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Contains("state=state%20value", url);
    }

    [Fact]
    public void BuildAuthorizeUrl_WithoutPkce_OmitsChallenge()
    {
        var url = OAuthClient.BuildAuthorizeUrl(Provider, new AuthorizeRequest("https://x/cb", ["openid"], "s"));

        Assert.DoesNotContain("code_challenge", url);
        Assert.DoesNotContain("nonce", url);
    }

    [Fact]
    public void MicrosoftPreset_UsesTenantInEndpointsAndTemplatedIssuer()
    {
        var provider = OAuthProvider.Microsoft("c", "s");

        Assert.Equal("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", provider.AuthorizationEndpoint);
        Assert.Equal("https://login.microsoftonline.com/organizations/oauth2/v2.0/token", provider.TokenEndpoint);
        Assert.Equal("https://login.microsoftonline.com/{tenantid}/v2.0", Assert.Single(provider.ValidIssuers));
    }

    [Fact]
    public async Task ExchangeCodeAsync_PostsFormAndParsesTokens()
    {
        var now = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        var http = new StubHttpHandler().RespondJson(
            """{"access_token":"at","refresh_token":"rt","id_token":"idt","expires_in":3600,"scope":"openid email","token_type":"Bearer"}""");
        var client = new OAuthClient(new HttpClient(http), new FixedTime(now));

        var tokens = await client.ExchangeCodeAsync(Provider, "the-code", "https://x/cb", "verifier");

        var (method, url, body) = Assert.Single(http.Requests);
        var form = ParseForm(body);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://oauth2.googleapis.com/token", url);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("https://x/cb", form["redirect_uri"]);
        Assert.Equal("verifier", form["code_verifier"]);
        Assert.Equal("client-1", form["client_id"]);
        Assert.Equal("secret-1", form["client_secret"]);

        Assert.Equal("at", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
        Assert.Equal("idt", tokens.IdToken);
        Assert.Equal(now.AddHours(1), tokens.ExpiresAt);
        Assert.Equal("openid email", tokens.Scope);
    }

    [Fact]
    public async Task RefreshAsync_PostsRefreshGrant_AndToleratesStringExpiry()
    {
        var http = new StubHttpHandler().RespondJson("""{"access_token":"at2","expires_in":"1800"}""");
        var client = new OAuthClient(new HttpClient(http));

        var tokens = await client.RefreshAsync(Provider, "old-refresh");

        var form = ParseForm(http.Requests.Single().Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("old-refresh", form["refresh_token"]);
        Assert.Equal("at2", tokens.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.NotNull(tokens.ExpiresAt);
    }

    [Fact]
    public async Task RefreshAsync_InvalidGrant_ThrowsOAuthExceptionWithErrorCode()
    {
        var http = new StubHttpHandler().RespondJson(
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""",
            HttpStatusCode.BadRequest);
        var client = new OAuthClient(new HttpClient(http));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshAsync(Provider, "revoked"));

        Assert.Equal(OAuthException.InvalidGrant, ex.Error);
        Assert.Equal("Token has been expired or revoked.", ex.Description);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ExchangeCodeAsync_NonJsonFailure_ThrowsInvalidResponse()
    {
        var http = new StubHttpHandler().RespondText("<html>Bad Gateway</html>", HttpStatusCode.BadGateway);
        var client = new OAuthClient(new HttpClient(http));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.ExchangeCodeAsync(Provider, "c", "https://x/cb"));

        Assert.Equal("invalid_response", ex.Error);
    }

    [Fact]
    public async Task ExchangeCodeAsync_SuccessWithoutAccessToken_Throws()
    {
        var http = new StubHttpHandler().RespondJson("""{"token_type":"Bearer"}""");
        var client = new OAuthClient(new HttpClient(http));

        await Assert.ThrowsAsync<OAuthException>(() => client.ExchangeCodeAsync(Provider, "c", "https://x/cb"));
    }
}
