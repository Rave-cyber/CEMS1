using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CEMS.Services
{
    public interface ILoginAttemptTracker
    {
        Task<int> GetFailedAttemptsAsync(string email);
        Task<bool> IsLockedOutAsync(string email);
        Task<int?> GetRemainingSecondsAsync(string email);
        Task RecordFailedAttemptAsync(string email);
        Task ClearAttemptsAsync(string email);
    }

    public class LoginAttemptTracker : ILoginAttemptTracker
    {
        private readonly IMemoryCache _cache;
        private const int MAX_ATTEMPTS = 3;
        private const int LOCKOUT_SECONDS = 30;
        private const string ATTEMPT_KEY_PREFIX = "login_attempt_";
        private const string LOCKOUT_KEY_PREFIX = "login_lockout_";

        public LoginAttemptTracker(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task<int> GetFailedAttemptsAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var key = ATTEMPT_KEY_PREFIX + normalizedEmail;

            if (_cache.TryGetValue(key, out int attempts))
            {
                return attempts;
            }

            return 0;
        }

        public async Task<bool> IsLockedOutAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var key = LOCKOUT_KEY_PREFIX + normalizedEmail;

            if (_cache.TryGetValue(key, out DateTime lockoutTime))
            {
                if (DateTime.UtcNow < lockoutTime)
                {
                    return true;
                }
                else
                {
                    // Lockout expired, clear it
                    _cache.Remove(key);
                    _cache.Remove(ATTEMPT_KEY_PREFIX + normalizedEmail);
                }
            }

            return false;
        }

        public async Task<int?> GetRemainingSecondsAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var key = LOCKOUT_KEY_PREFIX + normalizedEmail;

            if (_cache.TryGetValue(key, out DateTime lockoutTime))
            {
                var remaining = (int)(lockoutTime - DateTime.UtcNow).TotalSeconds;
                if (remaining > 0)
                {
                    return remaining;
                }
                else
                {
                    _cache.Remove(key);
                    _cache.Remove(ATTEMPT_KEY_PREFIX + normalizedEmail);
                }
            }

            return null;
        }

        public async Task RecordFailedAttemptAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var attemptKey = ATTEMPT_KEY_PREFIX + normalizedEmail;
            var lockoutKey = LOCKOUT_KEY_PREFIX + normalizedEmail;

            // Get current attempt count
            if (!_cache.TryGetValue(attemptKey, out int attempts))
            {
                attempts = 0;
            }

            attempts++;

            if (attempts >= MAX_ATTEMPTS)
            {
                // Lock out the user for LOCKOUT_SECONDS
                var lockoutTime = DateTime.UtcNow.AddSeconds(LOCKOUT_SECONDS);
                _cache.Set(lockoutKey, lockoutTime, TimeSpan.FromSeconds(LOCKOUT_SECONDS + 5));
                _cache.Set(attemptKey, attempts, TimeSpan.FromSeconds(LOCKOUT_SECONDS + 5));
            }
            else
            {
                // Keep track of attempts for 24 hours (they'll be cleared if successful or after lockout)
                _cache.Set(attemptKey, attempts, TimeSpan.FromHours(24));
            }
        }

        public async Task ClearAttemptsAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var attemptKey = ATTEMPT_KEY_PREFIX + normalizedEmail;
            var lockoutKey = LOCKOUT_KEY_PREFIX + normalizedEmail;

            _cache.Remove(attemptKey);
            _cache.Remove(lockoutKey);
        }

        private string NormalizeEmail(string email)
        {
            return email?.ToLower().Trim() ?? "";
        }
    }
}
