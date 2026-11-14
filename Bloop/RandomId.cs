using System.Security.Cryptography;

namespace Bloop
{
    public static class RandomId
    {
        // Excludes I, O and 0-lookalikes to keep ids unambiguous when read aloud.
        public const string UnambiguousUppercase = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        public const string Alphanumeric = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static string Generate(int length, string alphabet)
        {
            return string.Create(length, alphabet, static (span, chars) =>
            {
                for (var i = 0; i < span.Length; i++)
                    span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            });
        }
    }
}
