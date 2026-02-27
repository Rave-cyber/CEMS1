using CEMS.Data;
using CEMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CEMS.Services
{
    public class NotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public NotificationService(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        /// <summary>Driver submitted an expense report → notify all Managers.</summary>
        public async Task NotifyReportSubmitted(int reportId, decimal totalAmount, string driverUserName)
        {
            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            foreach (var m in managers)
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "New Expense Report Submitted",
                    Message = $"{driverUserName} submitted report #{reportId} (₱{totalAmount:N2}).",
                    UserId = m.Id,
                    Role = "Manager",
                    RelatedReportId = reportId,
                    Type = "ReportSubmitted"
                });
            }
            await _db.SaveChangesAsync();
        }

        /// <summary>Report exceeds budget → notify Manager, Finance, and CEO.</summary>
        public async Task NotifyReportOverBudget(int reportId, decimal totalAmount, string driverUserName)
        {
            var targets = new List<(string role, IList<IdentityUser> users)>
            {
                ("Manager", await _userManager.GetUsersInRoleAsync("Manager")),
                ("Finance", await _userManager.GetUsersInRoleAsync("Finance")),
                ("CEO", await _userManager.GetUsersInRoleAsync("CEO"))
            };

            foreach (var (role, users) in targets)
            {
                foreach (var u in users)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Title = "Over-Budget Report Submitted",
                        Message = $"Report #{reportId} by {driverUserName} (₱{totalAmount:N2}) exceeds the allowable budget.",
                        UserId = u.Id,
                        Role = role,
                        RelatedReportId = reportId,
                        Type = "ReportOverBudget"
                    });
                }
            }
            await _db.SaveChangesAsync();
        }

        /// <summary>Manager approved a report → notify Driver (and Finance if within budget).</summary>
        public async Task NotifyReportApprovedByManager(int reportId, string? driverUserId, bool isOverBudget)
        {
            if (!string.IsNullOrEmpty(driverUserId))
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "Report Approved by Manager",
                    Message = isOverBudget
                        ? $"Your report #{reportId} was approved by the manager and forwarded to the CEO for final approval."
                        : $"Your report #{reportId} was approved by the manager and sent to Finance for reimbursement.",
                    UserId = driverUserId,
                    Role = "Driver",
                    RelatedReportId = reportId,
                    Type = "ReportApprovedByManager"
                });
            }

            if (!isOverBudget)
            {
                var financeUsers = await _userManager.GetUsersInRoleAsync("Finance");
                foreach (var f in financeUsers)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Title = "Approved Report Awaiting Reimbursement",
                        Message = $"Report #{reportId} has been approved and is ready for reimbursement processing.",
                        UserId = f.Id,
                        Role = "Finance",
                        RelatedReportId = reportId,
                        Type = "ReportReadyForFinance"
                    });
                }
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>Manager rejected a report → notify Driver.</summary>
        public async Task NotifyReportRejectedByManager(int reportId, string? driverUserId)
        {
            if (string.IsNullOrEmpty(driverUserId)) return;

            _db.Notifications.Add(new Notification
            {
                Title = "Report Rejected by Manager",
                Message = $"Your report #{reportId} was rejected by the manager. Please review and resubmit.",
                UserId = driverUserId,
                Role = "Driver",
                RelatedReportId = reportId,
                Type = "ReportRejectedByManager"
            });
            await _db.SaveChangesAsync();
        }

        /// <summary>Manager forwarded an over-budget report to CEO → notify CEO.</summary>
        public async Task NotifyReportForwardedToCEO(int reportId, decimal totalAmount)
        {
            var ceos = await _userManager.GetUsersInRoleAsync("CEO");
            foreach (var c in ceos)
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "Over-Budget Report Requires Your Approval",
                    Message = $"Report #{reportId} (₱{totalAmount:N2}) has been forwarded to you for final approval.",
                    UserId = c.Id,
                    Role = "CEO",
                    RelatedReportId = reportId,
                    Type = "ReportForwardedToCEO"
                });
            }
            await _db.SaveChangesAsync();
        }

        /// <summary>CEO approved an over-budget report → notify Driver, Manager, Finance.</summary>
        public async Task NotifyCEOApproved(int reportId, string? driverUserId)
        {
            if (!string.IsNullOrEmpty(driverUserId))
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "Report Approved by CEO",
                    Message = $"Your over-budget report #{reportId} has been approved by the CEO and forwarded to Finance for reimbursement.",
                    UserId = driverUserId,
                    Role = "Driver",
                    RelatedReportId = reportId,
                    Type = "CEOApproved"
                });
            }

            var targets = new List<(string role, IList<IdentityUser> users)>
            {
                ("Manager", await _userManager.GetUsersInRoleAsync("Manager")),
                ("Finance", await _userManager.GetUsersInRoleAsync("Finance"))
            };

            foreach (var (role, users) in targets)
            {
                foreach (var u in users)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Title = "CEO Approved Over-Budget Report",
                        Message = $"Report #{reportId} has been approved by the CEO and is ready for reimbursement.",
                        UserId = u.Id,
                        Role = role,
                        RelatedReportId = reportId,
                        Type = "CEOApproved"
                    });
                }
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>CEO rejected an over-budget report → notify Driver and Manager.</summary>
        public async Task NotifyCEORejected(int reportId, string? driverUserId)
        {
            if (!string.IsNullOrEmpty(driverUserId))
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "Report Rejected by CEO",
                    Message = $"Your over-budget report #{reportId} was rejected by the CEO.",
                    UserId = driverUserId,
                    Role = "Driver",
                    RelatedReportId = reportId,
                    Type = "CEORejected"
                });
            }

            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            foreach (var m in managers)
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "CEO Rejected Over-Budget Report",
                    Message = $"Report #{reportId} was rejected by the CEO.",
                    UserId = m.Id,
                    Role = "Manager",
                    RelatedReportId = reportId,
                    Type = "CEORejected"
                });
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>Finance released payment → notify Driver and Manager.</summary>
        public async Task NotifyReimbursementProcessed(int reportId, string? driverUserId, decimal amount)
        {
            if (!string.IsNullOrEmpty(driverUserId))
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "Payment Released",
                    Message = $"Your report #{reportId} (₱{amount:N2}) has been reimbursed.",
                    UserId = driverUserId,
                    Role = "Driver",
                    RelatedReportId = reportId,
                    Type = "ReimbursementProcessed"
                });
            }

            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            foreach (var m in managers)
            {
                _db.Notifications.Add(new Notification
                {
                    Title = "Reimbursement Processed",
                    Message = $"Report #{reportId} (₱{amount:N2}) has been reimbursed by Finance.",
                    UserId = m.Id,
                    Role = "Manager",
                    RelatedReportId = reportId,
                    Type = "ReimbursementProcessed"
                });
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>Budget threshold reached (e.g. 80%) → notify CEO.</summary>
        public async Task NotifyBudgetThreshold(string category, decimal allocated, decimal spent, int percentUsed)
        {
            var ceos = await _userManager.GetUsersInRoleAsync("CEO");
            foreach (var c in ceos)
            {
                // Avoid duplicate threshold notifications for same category within last 24 hours
                var recentExists = await _db.Notifications
                    .AnyAsync(n => n.UserId == c.Id
                        && n.Type == "BudgetThreshold"
                        && n.Message.Contains(category)
                        && n.CreatedAt >= DateTime.UtcNow.AddHours(-24));

                if (!recentExists)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Title = "Budget Threshold Alert",
                        Message = $"Budget for '{category}' has reached {percentUsed}% (₱{spent:N2} of ₱{allocated:N2}).",
                        UserId = c.Id,
                        Role = "CEO",
                        Type = "BudgetThreshold"
                    });
                }
            }
            await _db.SaveChangesAsync();
        }
    }
}
