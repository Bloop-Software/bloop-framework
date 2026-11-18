using System.Security.Cryptography;
using System.Text;

namespace Bloop.Security
{
    // AES-256-GCM with a single locally held key. Suits tests and self-hosted deployments
    // where no key management service is available.
    public sealed class LocalAesSecretProtector : ISecretProtector
    {
        private const byte FormatVersion = 1;
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int HeaderSize = 1 + NonceSize + TagSize;

        private readonly byte[] _key;

        public LocalAesSecretProtector(byte[] key)
        {
            if (key.Length != KeySize)
                throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));

            _key = key.ToArray();
        }

        public static LocalAesSecretProtector FromBase64Key(string base64Key) =>
            new(Convert.FromBase64String(base64Key));

        public static string GenerateBase64Key() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize));

        public Task<byte[]> ProtectAsync(
            byte[] plaintext,
            IReadOnlyDictionary<string, string>? context = null,
            CancellationToken cancellationToken = default)
        {
            var output = new byte[HeaderSize + plaintext.Length];
            output[0] = FormatVersion;

            var nonce = output.AsSpan(1, NonceSize);
            var tag = output.AsSpan(1 + NonceSize, TagSize);
            RandomNumberGenerator.Fill(nonce);

            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderSize), tag, SerializeContext(context));

            return Task.FromResult(output);
        }

        public Task<byte[]> UnprotectAsync(
            byte[] ciphertext,
            IReadOnlyDictionary<string, string>? context = null,
            CancellationToken cancellationToken = default)
        {
            if (ciphertext.Length < HeaderSize || ciphertext[0] != FormatVersion)
                throw new CryptographicException("Unrecognised protected secret format");

            var plaintext = new byte[ciphertext.Length - HeaderSize];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                ciphertext.AsSpan(1, NonceSize),
                ciphertext.AsSpan(HeaderSize),
                ciphertext.AsSpan(1 + NonceSize, TagSize),
                plaintext,
                SerializeContext(context));

            return Task.FromResult(plaintext);
        }

        // Length-prefixed so that no two distinct contexts serialise to the same bytes.
        private static byte[] SerializeContext(IReadOnlyDictionary<string, string>? context)
        {
            if (context == null || context.Count == 0)
                return [];

            var builder = new StringBuilder();
            foreach (var pair in context.OrderBy(p => p.Key, StringComparer.Ordinal))
                builder.Append(pair.Key.Length).Append(':').Append(pair.Key)
                    .Append(pair.Value.Length).Append(':').Append(pair.Value);

            return Encoding.UTF8.GetBytes(builder.ToString());
        }
    }
}
