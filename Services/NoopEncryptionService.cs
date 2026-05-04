using System;

namespace CEMS.Services
{
    // A safe fallback when no encryption keys are configured.
    public class NoopEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText)
        {
            return plainText;
        }

        public string Decrypt(string cipherText)
        {
            return cipherText;
        }
    }
}
