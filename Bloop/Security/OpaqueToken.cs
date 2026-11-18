using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Bloop.Security
{
    // Random bearer secrets (refresh tokens, one-time codes). Only the hash is stored, so a
    // leaked database row cannot be replayed.
    public static class OpaqueToken
    {
        public static string Generate(int byteLength = 32) =>
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteLength));

        public static string Hash(string token) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        public static bool Matches(string token, string? expectedHash) =>
            expectedHash != null
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Hash(token)),
                Encoding.ASCII.GetBytes(expectedHash));
    }
}
