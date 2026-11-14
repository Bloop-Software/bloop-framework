using Bloop.Security;
using System.Security.Cryptography;
using Xunit;

namespace Bloop.Tests;

public class LocalAesSecretProtectorTests
{
    private static readonly Dictionary<string, string> Owner = new() { ["purpose"] = "calendar", ["ownerId"] = "42" };

    private readonly LocalAesSecretProtector _protector =
        LocalAesSecretProtector.FromBase64Key(LocalAesSecretProtector.GenerateBase64Key());

    [Fact]
    public async Task ProtectThenUnprotect_ReturnsOriginal()
    {
        var ciphertext = await _protector.ProtectStringAsync("refresh-token-value", Owner);

        Assert.Equal("refresh-token-value", await _protector.UnprotectStringAsync(ciphertext, Owner));
    }

    [Fact]
    public async Task Protect_SamePlaintextTwice_ProducesDifferentCiphertext()
    {
        var first = await _protector.ProtectStringAsync("same");
        var second = await _protector.ProtectStringAsync("same");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("same", System.Text.Encoding.UTF8.GetString(first));
    }

    [Fact]
    public async Task Unprotect_DifferentContext_Fails()
    {
        var ciphertext = await _protector.ProtectStringAsync("secret", Owner);
        var other = new Dictionary<string, string> { ["purpose"] = "calendar", ["ownerId"] = "43" };

        await Assert.ThrowsAnyAsync<CryptographicException>(() => _protector.UnprotectStringAsync(ciphertext, other));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => _protector.UnprotectStringAsync(ciphertext));
    }

    [Fact]
    public async Task Unprotect_ContextKeyValueBoundaryMoved_Fails()
    {
        var ciphertext = await _protector.ProtectStringAsync("secret", new Dictionary<string, string> { ["ab"] = "c" });

        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            _protector.UnprotectStringAsync(ciphertext, new Dictionary<string, string> { ["a"] = "bc" }));
    }

    [Fact]
    public async Task Unprotect_TamperedCiphertext_Fails()
    {
        var ciphertext = await _protector.ProtectStringAsync("secret");
        ciphertext[^1] ^= 0x01;

        await Assert.ThrowsAnyAsync<CryptographicException>(() => _protector.UnprotectStringAsync(ciphertext));
    }

    [Fact]
    public async Task Unprotect_DifferentKey_Fails()
    {
        var ciphertext = await _protector.ProtectStringAsync("secret");
        var other = LocalAesSecretProtector.FromBase64Key(LocalAesSecretProtector.GenerateBase64Key());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => other.UnprotectStringAsync(ciphertext));
    }

    [Fact]
    public async Task Unprotect_UnknownFormat_Fails()
    {
        await Assert.ThrowsAsync<CryptographicException>(() => _protector.UnprotectAsync(new byte[40]));
        await Assert.ThrowsAsync<CryptographicException>(() => _protector.UnprotectAsync([1, 2, 3]));
    }

    [Fact]
    public void Constructor_WrongKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LocalAesSecretProtector(new byte[16]));
    }
}

public class PkceTests
{
    [Fact]
    public void CreateChallenge_MatchesRfc7636Example()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verify_OwnVerifier_Succeeds_OtherFails()
    {
        var verifier = Pkce.CreateVerifier();
        var challenge = Pkce.CreateChallenge(verifier);

        Assert.InRange(verifier.Length, 43, 128);
        Assert.True(Pkce.Verify(verifier, challenge));
        Assert.False(Pkce.Verify(Pkce.CreateVerifier(), challenge));
        Assert.False(Pkce.Verify(verifier, "short"));
    }
}

public class OpaqueTokenTests
{
    [Fact]
    public void Generate_IsUrlSafeAndUnique()
    {
        var first = OpaqueToken.Generate();
        var second = OpaqueToken.Generate();

        Assert.NotEqual(first, second);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
    }

    [Fact]
    public void Matches_OnlyForTheHashedToken()
    {
        var token = OpaqueToken.Generate();
        var hash = OpaqueToken.Hash(token);

        Assert.Equal(64, hash.Length);
        Assert.True(OpaqueToken.Matches(token, hash));
        Assert.False(OpaqueToken.Matches(OpaqueToken.Generate(), hash));
        Assert.False(OpaqueToken.Matches(token, null));
    }
}
