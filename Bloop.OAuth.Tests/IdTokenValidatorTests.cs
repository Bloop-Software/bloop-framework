using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Bloop.OAuth.Tests;

public class IdTokenValidatorTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";

    private static readonly OAuthProvider Microsoft = OAuthProvider.Microsoft("client-ms", "secret");
    private static readonly OAuthProvider Google = OAuthProvider.Google("client-g", "secret");

    private sealed class SigningKey
    {
        private readonly RSA _rsa = RSA.Create(2048);

        public SigningKey(string keyId) => KeyId = keyId;

        public string KeyId { get; }

        public SigningCredentials Credentials =>
            new(new RsaSecurityKey(_rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256);

        public object PublicJwk
        {
            get
            {
                var parameters = _rsa.ExportParameters(false);
                return new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = KeyId,
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent),
                };
            }
        }
    }

    private static string Jwks(params SigningKey[] keys) =>
        JsonSerializer.Serialize(new { keys = keys.Select(k => k.PublicJwk) });

    private static string CreateToken(
        SigningKey key,
        string issuer,
        string audience,
        Dictionary<string, object> claims,
        DateTime? expires = null)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = key.Credentials,
        });
    }

    private static Dictionary<string, object> MicrosoftClaims(string nonce = "n-1") => new()
    {
        ["sub"] = "pairwise-subject",
        ["tid"] = TenantId,
        ["oid"] = "object-id-1",
        ["preferred_username"] = "dhiren@example.com",
        ["name"] = "Dhiren Example",
        ["nonce"] = nonce,
    };

    [Fact]
    public async Task ValidateAsync_MicrosoftToken_ResolvesTenantIssuerAndClaims()
    {
        var key = new SigningKey("k1");
        var http = new StubHttpHandler().RespondJson(Jwks(key));
        var validator = new IdTokenValidator(new HttpClient(http));
        var token = CreateToken(key, $"https://login.microsoftonline.com/{TenantId}/v2.0", "client-ms", MicrosoftClaims());

        var claims = await validator.ValidateAsync(Microsoft, token, "n-1");

        Assert.Equal("pairwise-subject", claims.Subject);
        Assert.Equal(TenantId, claims.TenantId);
        Assert.Equal("object-id-1", claims.ObjectId);
        Assert.Equal("dhiren@example.com", claims.Email);
        Assert.False(claims.EmailVerified);
        Assert.Equal("Dhiren Example", claims.Name);
        Assert.Equal("https://login.microsoftonline.com/organizations/discovery/v2.0/keys", http.Requests.Single().Url);
    }

    [Fact]
    public async Task ValidateAsync_GoogleToken_ReadsVerifiedEmailAndHostedDomain()
    {
        var key = new SigningKey("g1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, "https://accounts.google.com", "client-g", new()
        {
            ["sub"] = "1234567890",
            ["email"] = "dhiren@client.example",
            ["email_verified"] = true,
            ["hd"] = "client.example",
            ["given_name"] = "Dhiren",
            ["family_name"] = "Example",
        });

        var claims = await validator.ValidateAsync(Google, token);

        Assert.Equal("1234567890", claims.Subject);
        Assert.Null(claims.TenantId);
        Assert.True(claims.EmailVerified);
        Assert.Equal("client.example", claims.HostedDomain);
        Assert.Equal("Dhiren", claims.GivenName);
        Assert.Equal("Example", claims.FamilyName);
    }

    [Fact]
    public async Task ValidateAsync_EmailVerifiedAsString_IsAccepted()
    {
        var key = new SigningKey("g1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, "https://accounts.google.com", "client-g",
            new() { ["sub"] = "1", ["email_verified"] = "true" });

        Assert.True((await validator.ValidateAsync(Google, token)).EmailVerified);
    }

    [Fact]
    public async Task ValidateAsync_KeysAreCachedAcrossCalls()
    {
        var key = new SigningKey("k1");
        var http = new StubHttpHandler().RespondJson(Jwks(key));
        var validator = new IdTokenValidator(new HttpClient(http));
        var token = CreateToken(key, "https://accounts.google.com", "client-g", new() { ["sub"] = "1" });

        await validator.ValidateAsync(Google, token);
        await validator.ValidateAsync(Google, token);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKeyId_RefetchesKeysOnce()
    {
        var oldKey = new SigningKey("old");
        var newKey = new SigningKey("new");
        var http = new StubHttpHandler().RespondJson(Jwks(oldKey)).RespondJson(Jwks(oldKey, newKey));
        var validator = new IdTokenValidator(new HttpClient(http));

        await validator.ValidateAsync(Google,
            CreateToken(oldKey, "https://accounts.google.com", "client-g", new() { ["sub"] = "1" }));
        var claims = await validator.ValidateAsync(Google,
            CreateToken(newKey, "https://accounts.google.com", "client-g", new() { ["sub"] = "2" }));

        Assert.Equal("2", claims.Subject);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task ValidateAsync_SignedByUnpublishedKey_Fails()
    {
        var published = new SigningKey("k1");
        var attacker = new SigningKey("k1");
        var http = new StubHttpHandler().RespondJson(Jwks(published)).RespondJson(Jwks(published));
        var validator = new IdTokenValidator(new HttpClient(http));
        var token = CreateToken(attacker, "https://accounts.google.com", "client-g", new() { ["sub"] = "1" });

        await AssertInvalidAsync(() => validator.ValidateAsync(Google, token));
    }

    [Fact]
    public async Task ValidateAsync_WrongAudience_Fails()
    {
        var key = new SigningKey("k1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, "https://accounts.google.com", "someone-else", new() { ["sub"] = "1" });

        await AssertInvalidAsync(() => validator.ValidateAsync(Google, token));
    }

    [Fact]
    public async Task ValidateAsync_IssuerForDifferentTenantThanTidClaim_Fails()
    {
        var key = new SigningKey("k1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, "https://login.microsoftonline.com/other-tenant/v2.0", "client-ms", MicrosoftClaims());

        await AssertInvalidAsync(() => validator.ValidateAsync(Microsoft, token, "n-1"));
    }

    [Fact]
    public async Task ValidateAsync_MicrosoftTokenWithoutTid_Fails()
    {
        var key = new SigningKey("k1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, "https://login.microsoftonline.com/{tenantid}/v2.0", "client-ms",
            new() { ["sub"] = "1" });

        await AssertInvalidAsync(() => validator.ValidateAsync(Microsoft, token));
    }

    [Fact]
    public async Task ValidateAsync_Expired_Fails()
    {
        var key = new SigningKey("k1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, "https://accounts.google.com", "client-g", new() { ["sub"] = "1" },
            expires: DateTime.UtcNow.AddMinutes(-3));

        await AssertInvalidAsync(() => validator.ValidateAsync(Google, token));
    }

    [Fact]
    public async Task ValidateAsync_NonceMismatch_Fails()
    {
        var key = new SigningKey("k1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var token = CreateToken(key, $"https://login.microsoftonline.com/{TenantId}/v2.0", "client-ms",
            MicrosoftClaims(nonce: "attacker-nonce"));

        await AssertInvalidAsync(() => validator.ValidateAsync(Microsoft, token, "n-1"));
    }

    [Fact]
    public async Task ValidateAsync_UnsignedToken_Fails()
    {
        var key = new SigningKey("k1");
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler().RespondJson(Jwks(key))));
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com",
            Audience = "client-g",
            Claims = new Dictionary<string, object> { ["sub"] = "1" },
            Expires = DateTime.UtcNow.AddMinutes(30),
        });

        await AssertInvalidAsync(() => validator.ValidateAsync(Google, unsigned));
    }

    [Fact]
    public async Task ValidateAsync_Garbage_Fails()
    {
        var validator = new IdTokenValidator(new HttpClient(new StubHttpHandler()));

        await AssertInvalidAsync(() => validator.ValidateAsync(Google, "not-a-jwt"));
    }

    private static async Task AssertInvalidAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<OAuthException>(action);
        Assert.Equal(OAuthException.InvalidIdToken, ex.Error);
    }
}
