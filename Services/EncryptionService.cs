using System.Security.Cryptography;
using System.Text;

namespace CEMS.Services
{
    public interface IEncryptionService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }

    public class EncryptionService : IEncryptionService
    {
        private readonly IConfiguration _configuration;
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public EncryptionService(IConfiguration configuration)
        {
            _configuration = configuration;
            
            // Get encryption key from configuration (must be 32 bytes for AES-256)
            string keyString = _configuration["Encryption:Key"] 
                ?? throw new InvalidOperationException("Encryption:Key not configured. Run: dotnet user-secrets set \"Encryption:Key\" \"[base64-key]\"");
            
            // Get IV from configuration (must be 16 bytes)
            string ivString = _configuration["Encryption:IV"] 
                ?? throw new InvalidOperationException("Encryption:IV not configured. Run: dotnet user-secrets set \"Encryption:IV\" \"[base64-iv]\"");
            
            _key = Convert.FromBase64String(keyString);
            _iv = Convert.FromBase64String(ivString);
            
            if (_key.Length != 32)
                throw new InvalidOperationException("AES-256 key must be exactly 32 bytes");
            
            if (_iv.Length != 16)
                throw new InvalidOperationException("IV must be exactly 16 bytes");
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using (var aes = new AesCryptoServiceProvider())
                {
                    aes.Key = _key;
                    aes.IV = _iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream())
                    {
                        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        using (var sw = new StreamWriter(cs))
                        {
                            sw.Write(plainText);
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Encryption failed: {ex.Message}", ex);
            }
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                using (var aes = new AesCryptoServiceProvider())
                {
                    aes.Key = _key;
                    aes.IV = _iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(Convert.FromBase64String(cipherText)))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                // If decryption fails, return original (might be old unencrypted data)
                return cipherText;
            }
        }
    }
}
