using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Bloop.Security
{
    // Proof Key for Code Exchange (RFC 7636), S256 method.
    public static class Pkce
    {
        public const string Method = "S256";

        public static string CreateVerifier() =>
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        public static string CreateChallenge(string verifier) =>
            Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        public static bool Verify(string verifier, string challenge) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(CreateChallenge(verifier)),
                Encoding.ASCII.GetBytes(challenge));
    }
}
