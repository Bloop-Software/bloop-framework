using System.Text;

namespace Bloop.Security
{
    // Encrypts small secrets (tokens, keys, private URLs) for storage. The optional context
    // is bound to the ciphertext, so a value protected for one owner cannot be unprotected
    // under another.
    public interface ISecretProtector
    {
        Task<byte[]> ProtectAsync(
            byte[] plaintext,
            IReadOnlyDictionary<string, string>? context = null,
            CancellationToken cancellationToken = default);

        Task<byte[]> UnprotectAsync(
            byte[] ciphertext,
            IReadOnlyDictionary<string, string>? context = null,
            CancellationToken cancellationToken = default);
    }

    public static class SecretProtectorExtensions
    {
        extension(ISecretProtector protector)
        {
            public Task<byte[]> ProtectStringAsync(
                string plaintext,
                IReadOnlyDictionary<string, string>? context = null,
                CancellationToken cancellationToken = default) =>
                protector.ProtectAsync(Encoding.UTF8.GetBytes(plaintext), context, cancellationToken);

            public async Task<string> UnprotectStringAsync(
                byte[] ciphertext,
                IReadOnlyDictionary<string, string>? context = null,
                CancellationToken cancellationToken = default) =>
                Encoding.UTF8.GetString(await protector.UnprotectAsync(ciphertext, context, cancellationToken));
        }
    }
}
