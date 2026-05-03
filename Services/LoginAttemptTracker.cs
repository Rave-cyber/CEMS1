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

        // IP-level tracking
        Task RecordFailedAttemptByIpAsync(string ipAddress);
        Task<bool> IsIpBlockedAsync(string ipAddress);
    }

    public class LoginAttemptTracker : ILoginAttemptTracker
    {
        private readonly IMemoryCache _cache;
        private const int MAX_ATTEMPTS = 5;           // raised from 3 — detector handles alerting
        private const int LOCKOUT_SECONDS = 300;      // 5 minutes (was 30 seconds)
        private const int IP_MAX_ATTEMPTS = 20;       // block an IP after 20 failures
        private const int IP_BLOCK_SECONDS = 900;     // 15 minutes IP block
        private const string ATTEMPT_KEY_PREFIX = "login_attempt_";
        private const string LOCKOUT_KEY_PREFIX  = "login_lockout_";
        private const string IP_ATTEMPT_PREFIX   = "ip_attempt_";
        private const string IP_BLOCK_PREFIX     = "ip_block_";

        public LoginAttemptTracker(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task<int> GetFailedAttemptsAsync(string email)
        {
            var key = ATTEMPT_KEY_PREFIX + NormalizeEmail(email);
            return _cache.TryGetValue(key, out int attempts) ? attempts : 0;
        }

        public async Task<bool> IsLockedOutAsync(string email)
        {
            var key = LOCKOUT_KEY_PREFIX + NormalizeEmail(email);
            if (_cache.TryGetValue(key, out DateTime lockoutTime))
            {
                if (DateTime.UtcNow < lockoutTime) return true;
                _cache.Remove(key);
                _cache.Remove(ATTEMPT_KEY_PREFIX + NormalizeEmail(email));
            }
            return false;
        }

        public async Task<int?> GetRemainingSecondsAsync(string email)
        {
            var key = LOCKOUT_KEY_PREFIX + NormalizeEmail(email);
            if (_cache.TryGetValue(key, out DateTime lockoutTime))
            {
                var remaining = (int)(lockoutTime - DateTime.UtcNow).TotalSeconds;
                if (remaining > 0) return remaining;
                _cache.Remove(key);
                _cache.Remove(ATTEMPT_KEY_PREFIX + NormalizeEmail(email));
            }
            return null;
        }

        public async Task RecordFailedAttemptAsync(string email)
        {
            var normalized    = NormalizeEmail(email);
            var attemptKey    = ATTEMPT_KEY_PREFIX + normalized;
            var lockoutKey    = LOCKOUT_KEY_PREFIX + normalized;

            if (!_cache.TryGetValue(attemptKey, out int attempts)) attempts = 0;
            attempts++;

            if (attempts >= MAX_ATTEMPTS)
            {
                var lockoutTime = DateTime.UtcNow.AddSeconds(LOCKOUT_SECONDS);
                _cache.Set(lockoutKey, lockoutTime, TimeSpan.FromSeconds(LOCKOUT_SECONDS + 10));
                _cache.Set(attemptKey, attempts,    TimeSpan.FromSeconds(LOCKOUT_SECONDS + 10));
            }
            else
            {
                _cache.Set(attemptKey, attempts, TimeSpan.FromHours(24));
            }
        }

        public async Task ClearAttemptsAsync(string email)
        {
            var normalized = NormalizeEmail(email);
            _cache.Remove(ATTEMPT_KEY_PREFIX + normalized);
            _cache.Remove(LOCKOUT_KEY_PREFIX + normalized);
        }

        public async Task RecordFailedAttemptByIpAsync(string ipAddress)
        {
            var attemptKey = IP_ATTEMPT_PREFIX + ipAddress;
            var blockKey   = IP_BLOCK_PREFIX   + ipAddress;

            if (!_cache.TryGetValue(attemptKey, out int attempts)) attempts = 0;
            attempts++;

            if (attempts >= IP_MAX_ATTEMPTS)
            {
                var blockUntil = DateTime.UtcNow.AddSeconds(IP_BLOCK_SECONDS);
                _cache.Set(blockKey,   blockUntil, TimeSpan.FromSeconds(IP_BLOCK_SECONDS + 10));
                _cache.Set(attemptKey, attempts,   TimeSpan.FromSeconds(IP_BLOCK_SECONDS + 10));
            }
            else
            {
                _cache.Set(attemptKey, attempts, TimeSpan.FromHours(1));
            }
        }

        public async Task<bool> IsIpBlockedAsync(string ipAddress)
        {
            var blockKey = IP_BLOCK_PREFIX + ipAddress;
            if (_cache.TryGetValue(blockKey, out DateTime blockUntil))
            {
                if (DateTime.UtcNow < blockUntil) return true;
                _cache.Remove(blockKey);
                _cache.Remove(IP_ATTEMPT_PREFIX + ipAddress);
            }
            return false;
        }

        private static string NormalizeEmail(string email) =>
            email?.ToLower().Trim() ?? "";
    }
}
