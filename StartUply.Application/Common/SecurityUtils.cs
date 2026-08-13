using System.Security.Cryptography;
using System.Text;

namespace StartUply.Application.Common
{
    public static class SecurityUtils
    {
        /// <summary>
        /// Computes the SHA-256 hash of a string content (e.g. for file integrity verification or caching).
        /// </summary>
        public static string ComputeSha256(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Computes the SHA-256 hash of raw byte data.
        /// </summary>
        public static string ComputeSha256(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            byte[] hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Generates a safe, anonymous SHA-256 reference hash of a token or secret for log tracing.
        /// </summary>
        public static string HashToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return "null";
            string fullHash = ComputeSha256(token);
            return $"token-{fullHash[..12]}";
        }

        /// <summary>
        /// Masks a sensitive secret or PAT token, retaining only prefix and suffix characters for logging.
        /// </summary>
        public static string MaskSecret(string? secret)
        {
            if (string.IsNullOrWhiteSpace(secret)) return "****";
            if (secret.Length <= 8) return "****";
            return $"{secret[..4]}...{secret[^4..]}";
        }
    }
}
