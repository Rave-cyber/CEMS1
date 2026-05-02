# CEMS Security - Quick Reference Guide

**Last Updated**: May 3, 2026

---

## 🔐 Authentication & Authorization

### Login & Registration
```
✅ Email-based login/registration
✅ Password hashing (PBKDF2)
✅ Google OAuth 2.0 integration
✅ Email format validation
✅ Password minimum 6 characters
✅ Password confirmation required
```

### User Roles (5 Total)
```
1. SuperAdmin    - Full system access
2. CEO           - Executive oversight
3. Manager       - Department management
4. Finance       - Payment processing
5. Driver        - Expense submission
```

### Access Control Example
```csharp
[Authorize(Roles = "Driver")]              // Any authenticated Driver
[Authorize(Roles = "CEO")]                 // CEO only
[Authorize(Roles = "Finance, CEO")]        // Either role
```

---

## 🛡️ Data Encryption

### In Transit
```
🔒 HTTPS/TLS 1.3 Enforced
🔒 HSTS Headers Enabled
🔒 Secure Cookie Flags Set
```

### At Rest
```
🔑 User Secrets: DPAPI encrypted
🔑 Database: SQL Server encryption
🔑 Passwords: PBKDF2 hashed
🔑 Tokens: AES encrypted fields
```

### Key Storage
```
Development:  C:\Users\[User]\AppData\Roaming\Microsoft\UserSecrets\
Production:   Environment variables
              Docker secrets
              Azure Key Vault
```

---

## ✓ Input Validation

### Data Annotations Used
```csharp
[Required]           // Cannot be empty
[EmailAddress]       // Valid email format
[StringLength(100)]  // Max 100 characters
[MaxLength(50)]      // Max 50 characters
[MinLength(6)]       // Min 6 characters
[DataType(DataType.Password)]  // Password field
[Compare("Password")] // Match confirmation
```

### Validation Examples
```
Email:        ✅ user@example.com  ❌ user@  ❌ user.email.com
Password:     ✅ TestPass123       ❌ Test    ❌ (blank)
Amount:       ✅ 1000.00           ❌ -500    ❌ ABC
Category:     ✅ Travel            ❌ (blank)
```

### Validation Layers
```
Client-side:  jQuery Validation Unobtrusive
Server-side:  ASP.NET ModelState validation
Database:     Data type constraints
API:          Request validation
```

---

## 🚪 Login Attempt Protection

### Lockout Policy
```
Max Attempts:        3 failed attempts
Lockout Duration:    30 seconds
Auto-unlock:         After 30 seconds
Cache Storage:       In-memory
```

### Login Flow
```
Attempt 1: ❌ Invalid password → "Failed Attempts: 1/3"
Attempt 2: ❌ Invalid password → "Failed Attempts: 2/3"
Attempt 3: ❌ Invalid password → "Failed Attempts: 3/3" → LOCKED
Wait 30s:  ⏳ Countdown timer
Attempt 4: ✅ Correct password → Login successful
```

---

## 🔍 Code Auditing Results

### Security Code Scan (SCS) Findings

| Issue ID | Severity | Status | Location | Fix |
|----------|----------|--------|----------|-----|
| SCS0016 | Medium | ✅ Fixed | DriverController.cs(594) | Added `[ValidateAntiForgeryToken]` |
| SCS0005 | Low | ℹ️ Noted | SeedData.cs(134) | Test data only |
| SCS0027 | Medium | 🔄 WIP | ReceiptController.cs (6×) | Validate URL redirects |

### Total Issues: 8 Warnings (6 Open Redirect, 1 CSRF, 1 Weak RNG)

---

## 📋 Error Handling & Logging

### Exception Handling
```csharp
try
{
    // Business logic
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error message");
    TempData["Error"] = "User-friendly message";
    return RedirectToAction("Index");
}
```

### Logged Events
```
✅ User login attempts (success/failure)
✅ Account creation and modifications
✅ Role assignments
✅ Expense submissions and changes
✅ Approvals and rejections
✅ Payment processing
✅ Data access and changes
✅ Errors and exceptions
```

### Audit Log Model
```csharp
public class AuditLog
{
    public int Id { get; set; }
    public string Action { get; set; }              // "Login", "Submit", etc.
    public string Module { get; set; }              // "Auth", "Expense", etc.
    public string Role { get; set; }                // "Driver", "CEO", etc.
    public string PerformedByUserId { get; set; }   // Who did it
    public string TargetUserId { get; set; }        // On whom
    public string Details { get; set; }             // What changed
    public int RelatedRecordId { get; set; }        // Report/Expense ID
    public DateTime Timestamp { get; set; }         // When
}
```

---

## 🎯 Protected Pages & Routes

### Authentication Required (All)
```
/Driver/*               All Driver pages
/CEO/*                  All CEO pages
/Finance/*              All Finance pages
/Manager/*              All Manager pages
/SuperAdmin/*           All Admin pages
/Profile/*              User profile pages
```

### Public Pages (No Auth)
```
/                       Home page
/Home/Privacy           Privacy policy
/Identity/Account/Login         Login page
/Identity/Account/Register      Registration page
/Identity/Account/ForgotPassword    Password reset
```

### Authorization Example
```
URL: /CEO/Dashboard
User: Driver
Result: ❌ 403 Forbidden → Redirect to AccessDenied

URL: /Driver/Dashboard
User: Driver
Result: ✅ 200 OK → Dashboard shown
```

---

## 🔒 Security Policies

### Password Requirements
```
Minimum Length:     6 characters
Character Mix:      Not enforced (can improve)
Special Characters: Not required
Expiration:         None (can add)
History:            None (can track)
```

### Login Attempt Policy
```
Max Failed Attempts:  3
Lockout Duration:     30 seconds
Attempt Window:       No timeout (can add)
Notification:         None (can add email)
```

### Data Handling Policy
```
Public Data:      Names, roles, departments
Confidential:     Emails, phone, expenses
Secret:           Passwords, API keys, tokens

Access Control:   Role-based restrictions
Data Classification: 3-tier system
Retention:        Varies by data type
Deletion:         Manual or automatic
```

### Data Retention Schedule
```
Audit Logs:       2 years (with archival)
Expense Reports:  Indefinite (yearly archives)
Sessions:         30 minutes (auto-expire)
Payments:         7 years (PCI-DSS requirement)
OAuth Tokens:     Current use only
```

---

## 🧪 Security Testing Status

### Authentication Testing
```
✅ Valid login succeeds
✅ Invalid credentials rejected
✅ Account lockout after 3 failures
✅ Lockout expires after 30 seconds
✅ Session timeout after 30 minutes
✅ Google OAuth connection works
```

### Authorization Testing
```
✅ Driver cannot access CEO dashboard
✅ Users cannot access other users' data
✅ Role-based restrictions enforced
✅ SuperAdmin has full access
✅ Unauthorized access shows 403
```

### Input Validation Testing
```
✅ Email format validated
✅ Password length checked
✅ Required fields enforced
✅ Text length limits respected
✅ Amount validation (positive)
✅ HTML injection prevented
```

### Data Security Testing
```
✅ HTTPS enforced
✅ TLS 1.3 active
✅ Secure cookies set
✅ User secrets encrypted
✅ Passwords hashed
```

---

## 📊 Security Implementation Summary

### Implemented Features
```
✅ HTTPS/TLS 1.3 encryption
✅ ASP.NET Identity authentication
✅ Role-based authorization (RBAC)
✅ Password hashing (PBKDF2)
✅ Input validation (data annotations)
✅ CSRF protection ([ValidateAntiForgeryToken])
✅ Login attempt lockout (3 strikes)
✅ Session timeout (30 minutes)
✅ Secure cookies (HttpOnly, Secure flags)
✅ Audit logging
✅ Google OAuth 2.0
✅ User secrets encryption
✅ Environment variable support
```

### Ready for Enhancement
```
⚠️ URL validation for open redirects
⚠️ Two-factor authentication (2FA)
⚠️ Additional security headers (CSP, X-Frame-Options)
⚠️ Rate limiting on endpoints
⚠️ IP whitelisting
⚠️ Biometric authentication
⚠️ Encrypted database columns
⚠️ Regular penetration testing
```

---

## 🚀 Quick Start: Enable a Security Feature

### Enable Strict HTTPS
```csharp
// In Program.cs
app.UseHttpsRedirection();
app.UseHsts();
```

### Add Authorization to Endpoint
```csharp
[Authorize]                    // Any authenticated user
[Authorize(Roles = "Driver")]  // Specific role
public async Task<IActionResult> MyAction() { }
```

### Add Input Validation
```csharp
[Required]
[EmailAddress]
public string Email { get; set; }
```

### Log an Action
```csharp
_logger.LogInformation("User {UserId} submitted expense", userId);
```

### Check Authorization
```csharp
var userId = _userManager.GetUserId(User);
var isAuthorized = User.IsInRole("Driver");
```

---

## 📚 Documentation Files

| File | Purpose |
|------|---------|
| `COMPREHENSIVE_SECURITY_DOCUMENTATION.md` | Complete security details (this guide) |
| `SECURITY_SCREENSHOTS_GUIDE.md` | How to capture security verification screenshots |
| `SECURITY_HARDENING_SUMMARY.md` | Secrets removal and hardening steps |
| `SECURITY_VULNERABILITY_REPORT.md` | Detailed vulnerability findings |
| `SECRETS_MANAGEMENT.md` | User secrets and environment variables setup |

---

## 🔗 External Resources

- [ASP.NET Core Security Documentation](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [OWASP Top 10](https://owasp.org/Top10/)
- [Security Code Scan](https://security-code-scan.github.io/)
- [Microsoft Security Best Practices](https://docs.microsoft.com/en-us/security/)
- [Google OAuth 2.0](https://developers.google.com/identity/protocols/oauth2)

---

## ✅ Verification Checklist

Use this before production deployment:

```
Authentication
  ☐ Login works with valid credentials
  ☐ Login fails with invalid credentials
  ☐ Password reset flow works
  ☐ Account lockout works (3 attempts)
  ☐ Google OAuth works

Authorization
  ☐ Each role can access their dashboards
  ☐ Cross-role access is blocked
  ☐ Unauthorized users see error page

Data Security
  ☐ HTTPS is enforced
  ☐ Cookies are secure/HttpOnly
  ☐ All secrets are in User Secrets or env vars
  ☐ No secrets in source code

Validation
  ☐ Invalid emails rejected
  ☐ Short passwords rejected
  ☐ Required fields enforced
  ☐ Data length limits respected

Logging
  ☐ Login attempts logged
  ☐ Errors logged without sensitive data
  ☐ Audit trail exists
  ☐ Logs are accessible

Testing
  ☐ All security tests pass
  ☐ No console warnings/errors
  ☐ Security scan shows no critical issues
```

---

**Version**: 1.0  
**Created**: May 3, 2026  
**Last Updated**: May 3, 2026  
**Status**: ✅ Complete - Ready for Review
