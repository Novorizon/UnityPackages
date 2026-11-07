using System;
using System.Security.Cryptography;
using System.Text;

namespace Game.Framework.Auth
{
    public static class IdentityNonce
    {
        public static string Create()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string Sha256(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("A nonce is required.", nameof(raw));
            }
            using (var hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var result = new StringBuilder(64);
                foreach (byte value in digest)
                {
                    result.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }
    }
}
