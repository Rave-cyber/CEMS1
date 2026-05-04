using CEMS.Data;
using CEMS.Models;
using CEMS.Services;
using Microsoft.EntityFrameworkCore;

namespace CEMS.Services
{
    /// <summary>
    /// Detects suspicious activity patterns and raises SecurityAlert audit entries.
    /// Called after every login attempt (success or failure).
    /// </summary>
    public interface ISecurityThreatDetector
    {
        Task AnalyzeLoginAsync(string email, string? ipAddress, string? userAgent, bool succeeded);
        Task<SecurityThreatSummary> GetThreatSummaryAsync();
    }

    public class SecurityThreatSummary
    {
        public int FailedLoginsLast1h { get; set; }
        public int UniqueAttackerIpsLast1h { get; set; }
        public int LockedAccountsLast1h { get; set; }
        public int SuspiciousIpCount { get; set; }
        public List<ThreatEvent> RecentThreats { get; set; } = new();
    }

    public class ThreatEvent
    {
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
        public string? IpAddress { get; set; }
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "Medium"; // Low / Medium / High / Critical
    }

    public class SecurityThreatDetector : ISecurityThreatDetector
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SecurityThreatDetector> _logger;
        private readonly NotificationService _notificationService;

        // Thresholds
        private const int BruteForceWindowMinutes = 10;
        private const int BruteForceThreshold = 5;       // 5 failures from same IP in 10 min
        private const int CredentialStuffingThreshold = 10; // 10 different emails from same IP in 10 min
        private const int AccountEnumerationThreshold = 8;  // 8 failures on non-existent accounts in 10 min

        public SecurityThreatDetector(ApplicationDbContext db, ILogger<SecurityThreatDetector> logger, NotificationService notificationService)
        {
            _db = db;
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task AnalyzeLoginAsync(string email, string? ipAddress, string? userAgent, bool succeeded)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return;

            var windowStart = DateTime.UtcNow.AddMinutes(-BruteForceWindowMinutes);

            // ── 1. Brute Force: many failures from same IP ──────────────────────
            var failuresFromIp = await _db.AuditLogs
                .CountAsync(l => l.Action == "FailedLoginAttempt"
                              && l.IpAddress == ipAddress
                              && l.Timestamp >= windowStart);

            if (failuresFromIp >= BruteForceThreshold)
            {
                await RaiseThreatAsync(
                    action: "BruteForceDetected",
                    ipAddress: ipAddress,
                    userAgent: userAgent,
                    severity: "Critical",
                    details: $"Brute force attack detected: {failuresFromIp} failed login attempts from IP {ipAddress} in the last {BruteForceWindowMinutes} minutes. Target: {email}"
                );
            }

            // ── 2. Credential Stuffing: many different emails from same IP ──────
            var uniqueEmailsFromIp = await _db.AuditLogs
                .Where(l => l.Action == "FailedLoginAttempt"
                         && l.IpAddress == ipAddress
                         && l.Timestamp >= windowStart)
                .Select(l => l.Details)
                .Distinct()
                .CountAsync();

            if (uniqueEmailsFromIp >= CredentialStuffingThreshold)
            {
                await RaiseThreatAsync(
                    action: "CredentialStuffingDetected",
                    ipAddress: ipAddress,
                    userAgent: userAgent,
                    severity: "Critical",
                    details: $"Credential stuffing detected: {uniqueEmailsFromIp} different accounts targeted from IP {ipAddress} in {BruteForceWindowMinutes} minutes."
                );
            }

            // ── 3. Successful login after multiple failures (possible breach) ───
            if (succeeded)
            {
                var recentFailuresForEmail = await _db.AuditLogs
                    .CountAsync(l => l.Action == "FailedLoginAttempt"
                                  && l.Details!.Contains(email)
                                  && l.Timestamp >= DateTime.UtcNow.AddHours(-1));

                if (recentFailuresForEmail >= 3)
                {
                    await RaiseThreatAsync(
                        action: "SuspiciousLoginSuccess",
                        ipAddress: ipAddress,
                        userAgent: userAgent,
                        severity: "High",
                        details: $"Account '{email}' logged in successfully after {recentFailuresForEmail} failed attempts in the last hour from IP {ipAddress}. Possible compromised credentials."
                    );
                }
            }

            // ── 4. Login from new IP for an existing user (after prior logins) ──
            if (succeeded)
            {
                var knownIps = await _db.AuditLogs
                    .Where(l => l.Action == "UserLogin"
                             && l.Details!.Contains(email)
                             && l.IpAddress != null
                             && l.Timestamp < DateTime.UtcNow.AddMinutes(-1)) // exclude current
                    .Select(l => l.IpAddress)
                    .Distinct()
                    .ToListAsync();

                if (knownIps.Count > 0 && !knownIps.Contains(ipAddress))
                {
                    await RaiseThreatAsync(
                        action: "NewIpLogin",
                        ipAddress: ipAddress,
                        userAgent: userAgent,
                        severity: "Medium",
                        details: $"Account '{email}' logged in from a new IP address: {ipAddress}. Previously seen from: {string.Join(", ", knownIps.Take(3))}."
                    );
                }
            }
        }

        public async Task<SecurityThreatSummary> GetThreatSummaryAsync()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);

            var failedLogins = await _db.AuditLogs
                .Where(l => l.Action == "FailedLoginAttempt" && l.Timestamp >= oneHourAgo)
                .ToListAsync();

            var threats = await _db.AuditLogs
                .Where(l => (l.Action == "BruteForceDetected"
                          || l.Action == "CredentialStuffingDetected"
                          || l.Action == "SuspiciousLoginSuccess"
                          || l.Action == "NewIpLogin")
                         && l.Timestamp >= DateTime.UtcNow.AddHours(-24))
                .OrderByDescending(l => l.Timestamp)
                .Take(20)
                .ToListAsync();

            return new SecurityThreatSummary
            {
                FailedLoginsLast1h = failedLogins.Count,
                UniqueAttackerIpsLast1h = failedLogins
                    .Where(l => l.IpAddress != null)
                    .Select(l => l.IpAddress)
                    .Distinct()
                    .Count(),
                LockedAccountsLast1h = await _db.AuditLogs
                    .CountAsync(l => l.Action == "FailedLoginAttempt"
                                  && l.Timestamp >= oneHourAgo
                                  && l.Details!.Contains("5/5")),
                SuspiciousIpCount = threats
                    .Where(t => t.IpAddress != null)
                    .Select(t => t.IpAddress)
                    .Distinct()
                    .Count(),
                RecentThreats = threats.Select(t => new ThreatEvent
                {
                    Action = t.Action,
                    Details = t.Details ?? "",
                    IpAddress = t.IpAddress,
                    Timestamp = t.Timestamp,
                    Severity = t.Action switch
                    {
                        "BruteForceDetected" => "Critical",
                        "CredentialStuffingDetected" => "Critical",
                        "SuspiciousLoginSuccess" => "High",
                        "NewIpLogin" => "Medium",
                        _ => "Low"
                    }
                }).ToList()
            };
        }

        private async Task RaiseThreatAsync(string action, string? ipAddress, string? userAgent, string severity, string details)
        {
            // Deduplicate: don't raise the same threat from the same IP more than once per 5 minutes
            var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
            var alreadyRaised = await _db.AuditLogs
                .AnyAsync(l => l.Action == action
                            && l.IpAddress == ipAddress
                            && l.Timestamp >= fiveMinutesAgo);

            if (alreadyRaised) return;

            _logger.LogWarning("[SECURITY THREAT] {Action} | IP: {Ip} | {Details}", action, ipAddress, details);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                Module = "Security",
                Role = null,
                PerformedByUserId = null,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details = $"[{severity}] {details}"
            });

            await _db.SaveChangesAsync();

            // Notify all SuperAdmins about the detected threat
            try
            {
                await _notificationService.NotifySecurityThreat(action, severity, details, ipAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send security threat notification for action {Action}", action);
            }
        }
    }
}
