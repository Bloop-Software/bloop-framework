using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bloop.WebApi;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Bloop.AWS.Lambda.Tests;

public class JwtTokenValidatorTests
{
    internal const string Issuer = "test-issuer";
    internal const string Audience = "test-audience";
    internal const string PrivateKey = "test-private-key-that-is-long-enough-for-hmac-sha256";

    private readonly JwtTokenValidator _validator = new(Issuer, Audience, PrivateKey);

    internal static string CreateToken(
        Guid? userId = null,
        Guid? sessionId = null,
        string issuer = Issuer,
        string audience = Audience,
        string privateKey = PrivateKey,
        DateTime? expires = null,
        bool includeSub = true,
        bool includeSid = true)
    {
        var claims = new List<Claim>();
        if (includeSub)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, (userId ?? Guid.NewGuid()).ToString()));
        if (includeSid)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sid, (sessionId ?? Guid.NewGuid()).ToString()));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddHours(-4),
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(privateKey)),
                SecurityAlgorithms.HmacSha256Signature));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void Validate_ValidToken_ReturnsUserAndSessionIds()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var result = _validator.Validate(CreateToken(userId, sessionId));

        Assert.Equal(userId, result.UserId);
        Assert.Equal(sessionId, result.SessionId);
    }

    [Fact]
    public void Validate_WrongSigningKey_ThrowsUnauthorized()
    {
        var token = CreateToken(privateKey: "a-completely-different-key-also-long-enough-here");

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(token));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_TamperedSignature_ThrowsUnauthorized()
    {
        var token = CreateToken();
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(tampered));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_TamperedPayload_ThrowsUnauthorized()
    {
        var parts = CreateToken().Split('.');
        var payloadJson = $"{{\"sub\":\"{Guid.NewGuid()}\",\"sid\":\"{Guid.NewGuid()}\",\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\"}}";
        var forgedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=');
        var forged = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(forged));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_ExpiredToken_ThrowsUnauthorized()
    {
        var token = CreateToken(expires: DateTime.UtcNow.AddHours(-2));

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(token));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_WrongIssuer_ThrowsUnauthorized()
    {
        var token = CreateToken(issuer: "someone-else");

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(token));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_WrongAudience_ThrowsUnauthorized()
    {
        var token = CreateToken(audience: "someone-else");

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(token));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_MissingSubClaim_ThrowsUnauthorized()
    {
        var token = CreateToken(includeSub: false);

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(token));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_MissingSidClaim_ThrowsUnauthorized()
    {
        var token = CreateToken(includeSid: false);

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(token));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_Garbage_ThrowsUnauthorized()
    {
        var ex = Assert.Throws<WebApiException>(() => _validator.Validate("not-a-jwt"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void Validate_UnsignedToken_ThrowsUnauthorized()
    {
        var parts = CreateToken().Split('.');
        var unsigned = $"{parts[0]}.{parts[1]}.";

        var ex = Assert.Throws<WebApiException>(() => _validator.Validate(unsigned));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }
}
