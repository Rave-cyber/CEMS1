# CEMS Security Documentation — Sections 5 to 11

---

## 5. Input Validation and Sanitization

### What Inputs Are Validated?

CEMS validates inputs at two layers: **model-level** (DataAnnotations) and **controller-level** (manual checks).

#### Model-Level Validation (DataAnnotations)

| Model | Validated Fields | Rules Applied |
|---|---|---|
| `Expense` | Category, Amount, Date | `[Required]`; Description `[MaxLength(300)]` |
| `ExpenseItem` | Category, Amount, Date | `[Required]`; Description `[MaxLength(500)]` |
| `AuditLog` | Action | `[Required]`, `[MaxLength(200)]`; IpAddress `[MaxLength(45)]`; UserAgent `[MaxLength(500)]` |
| `Budget` | Category, Allocated | `[Required]`; Description `[MaxLength(300)]` |
| Profile models | FullName, ContactNumber, Address fields | `[Required]`, `[MaxLength]` on each field |
| `Login InputModel` | Email, Password | `[Required]`, `[EmailAddress]`, `[DataType(DataType.Password)]` |

#### Controller-Level Validation

- **Receipt file uploads** — validated for file size and content type before storage (DriverController `Submit` / `EditReport`).
- **Budget category uniqueness** — duplicate category names are rejected before saving.
- **UserId binding prevention** — `[BindNever]` is applied to `UserId`, `ReceiptData`, `ReceiptPath`, and `ReceiptContentType` on the `Expense` model, preventing mass-assignment attacks.
- **String normalization** — user-supplied strings are `.Trim()`-ed; empty optional strings are converted to `null` before persistence.
- **IP-level block check** — the login handler rejects the request immediately if the originating IP is blocked, before any model binding is processed.

#### Anti-Forgery Token Validation

Every state-changing POST action is decorated with `[ValidateAntiForgeryToken]`, covering:
- Expense submission and editing (DriverController)
- Approval / rejection / forwarding (ManagerController, CEOController)
- Reimbursement processing (FinanceController)
- User management, role assignment, lockout, deletion (SuperAdminController)
- Profile updates and photo uploads (ProfileController)
- Notification management (NotificationController)

### Tools / Libraries Used

| Tool / Library | Purpose |
|---|---|
| `System.ComponentModel.DataAnnotations` | Declarative field-level validation (`[Required]`, `[MaxLength]`, `[Range]`, `[EmailAddress]`) |
| `Microsoft.AspNetCore.Mvc.ModelBinding` | `[BindNever]` to block mass-assignment on sensitive fields |
| ASP.NET Core Model State (`ModelState.IsValid`) | Automatic validation pipeline — invalid models are rejected before action logic runs |
| ASP.NET Core Anti-Forgery Middleware | CSRF token generation and validation on all POST forms |
| `ILoginAttemptTracker` (custom service) | Rate-limiting and lockout enforcement at the login endpoint |

> Note: The project uses DataAnnotations exclusively. FluentValidation is not used.

### Rejected Invalid Input — Examples

Since screenshots cannot be embedded in a markdown document, the following describes the exact validation messages the system produces for invalid inputs:

**Empty required fields (e.g., submitting the expense form with no category):**
> "The Category field is required."

**Invalid email format on login:**
> "The Email field is not a valid e-mail address."

**Empty password on login:**
> "The Password field is required."

**Too many failed login attempts (account lockout):**
> "Too many failed attempts (5/5). Account locked for 300 seconds."

**IP-level block after 20 failures:**
> "Too many failed attempts from your network. Please try again later."

**Remaining attempts warning:**
> "Invalid login attempt. Attempts remaining: 2"

---

## 6. Error Handling and Logging

### How the System Handles Errors

CEMS uses a **layered error handling strategy**:

#### Environment-Specific Middleware

Configured in `Program.cs`:

```csharp
// Development — shows full exception details and migration errors
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    // Production — generic error page, no stack traces exposed to users
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
```

In production, users see only a generic error page. Stack traces and internal details are never exposed.

#### Try-Catch in Critical Operations

Controllers and Identity pages wrap sensitive operations in try-catch blocks to prevent unhandled exceptions from leaking details or blocking primary flows:

| Location | Operations Wrapped |
|---|---|
| `DriverController` | `Submit` (single expense) and `SubmitMultiple` (batch submission) |
| `FinanceController` | PayMongo payment sync, checkout creation, payment verification, manual payment status check (4 catch blocks) |
| `SuperAdminController` | `CreateBackup` and `RestoreBackup` — returns user-friendly `TempData["Error"]` on failure |
| `PayMongoWebhookController` | Webhook event processing and HMAC signature validation |
| `Login.cshtml.cs` | Audit log writes (success and failure) and both `AnalyzeLoginAsync` calls — wrapped in `catch { }` so a logging failure never blocks authentication |
| `Logout.cshtml.cs` | Audit log write on logout |

Controllers that perform only straightforward database reads/writes (`ManagerController`, `CEOController`, `HomeController`, `NotificationController`, `ProfileController`, `ReceiptController`) do not use try-catch locally — they rely on the global `UseExceptionHandler("/Home/Error")` middleware to handle unexpected exceptions.

The pattern is intentional: try-catch is applied where operations involve **external systems** (PayMongo API, AWS S3, database backup engine) or where a **secondary operation** (audit logging) must not block the primary user flow.

#### Database Resilience

EF Core is configured with automatic retry on transient SQL Server failures:

```csharp
sqlOptions.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(30),
    errorNumbersToAdd: null);
```

### What Logs Are Recorded

CEMS maintains two logging channels:

#### 1. ASP.NET Core Built-in Logging

Configured in `appsettings.json`:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}
```

- `Information` level and above for application code.
- `Warning` level and above for ASP.NET Core framework events (reduces noise).
- Logs are written to the console and captured by the hosting environment.

Key log entries written via `ILogger`:
- `"User logged in."` — on successful authentication.
- `"User account locked out."` — when ASP.NET Identity lockout triggers.

#### 2. Database Audit Log (`AuditLogs` table)

Every significant action is persisted to the database with the following fields:

| Field | Description |
|---|---|
| `Action` | Event name (e.g., `UserLogin`, `FailedLoginAttempt`, `BruteForceDetected`) |
| `Module` | Subsystem (e.g., `Auth`, `Expense`, `Budget`, `UserManagement`) |
| `Role` | Role of the actor |
| `PerformedByUserId` | Identity of the user who performed the action |
| `TargetUserId` | Identity of the affected user (for admin actions) |
| `Details` | Human-readable description including email, attempt count, IP |
| `RelatedRecordId` | FK to the affected record (expense report ID, etc.) |
| `Timestamp` | UTC timestamp |
| `IpAddress` | Client IP (supports IPv6, max 45 chars) |
| `UserAgent` | Browser/client string (max 500 chars) |

**Actions logged include:**

| Category | Actions |
|---|---|
| Authentication | `UserLogin`, `FailedLoginAttempt`, `BruteForceDetected`, `CredentialStuffingDetected`, `AccountEnumerationDetected`, `SuspiciousLoginSuccess`, `NewIpLogin` |
| User Management | `CreateAccount`, `AssignRole`, `RemoveRole`, `ToggleLockout`, `DeleteAccount` |
| Expense Workflow | `SubmitExpense`, `EditExpenseReport`, `ApproveReport`, `RejectReport`, `ForwardToCEO`, `CEOApprove`, `CEOReject` |
| Finance | `Reimburse`, `MarkReimbursedManual` |
| Budget | `CreateBudget`, `EditBudget`, `DeleteBudget` |
| Profile | `UpdateProfile`, `UploadProfilePhoto`, `GmailConnected`, `GmailDisconnected` |
| System | `CreateBackup`, `RestoreBackup`, `CreateFuelPrice`, `UpdateFuelPrice`, `DeleteFuelPrice` |

#### Security Threat Detection Logs

The `SecurityThreatDetector` service automatically raises threat entries with severity levels:

| Threat | Trigger | Severity |
|---|---|---|
| Brute Force | 5+ failures from same IP in 10 min | Critical |
| Credential Stuffing | 10+ different emails from same IP in 10 min | High |
| Account Enumeration | 8+ failures on non-existent accounts in 10 min | High |
| Suspicious Login Success | Login after 3+ failures in 1 hour | Medium |
| New IP Login | Login from a previously unseen IP | Low |

Threats are deduplicated — the same threat from the same IP is not re-raised within a 5-minute window.

### Viewing Logs

The SuperAdmin can view, filter, and export audit logs from the **Audit Logs** page (`/SuperAdmin/AuditLogs`). Filters include: action type, module, role, user, and date range. Export to PDF is supported.

---

## 7. Access Control

### What Pages Are Protected?

Every controller except `HomeController` (public landing) and the Identity pages (login/register) requires authentication. Role-based authorization is enforced at the controller class level.

| Controller | Protection | Allowed Roles |
|---|---|---|
| `DriverController` | `[Authorize(Roles = "Driver")]` | Driver only |
| `ManagerController` | `[Authorize(Roles = "Manager")]` | Manager only |
| `CEOController` | `[Authorize(Roles = "CEO")]` | CEO only |
| `FinanceController` | `[Authorize(Roles = "Finance")]` | Finance only |
| `SuperAdminController` | `[Authorize(Roles = "SuperAdmin")]` | SuperAdmin only |
| `ReceiptController` | `[Authorize]` | Any authenticated user |
| `ProfileController` | `[Authorize]` | Any authenticated user |
| `NotificationController` | `[Authorize]` | Any authenticated user |
| `HomeController.GoToDashboard` | `[Authorize]` | Any authenticated user |

### How Unauthorized Access Is Prevented

**1. Role-Based Authorization Attributes**
Controllers are decorated at the class level, so every action within them inherits the restriction. A Driver attempting to access `/Manager/Dashboard` receives an HTTP 403 and is redirected to `/Home/AccessDenied`.

**2. Cookie-Based Authentication Redirect**
Configured in `Program.cs`:
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.LoginPath = "/Identity/Account/Login";
});
```
Unauthenticated requests are redirected to the login page. Authenticated users without the required role are redirected to the Access Denied page.

**3. Resource-Level Ownership Checks**
Within the Driver role, controllers additionally verify that the requested resource belongs to the authenticated user:
```csharp
var report = await _db.ExpenseReports
    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
if (report == null) return NotFound();
```
This prevents horizontal privilege escalation (Driver A accessing Driver B's reports).

**4. Stale Session Detection**
`HomeController.Index` checks whether the authenticated user still exists in the database on every visit. If the account has been deleted, the session is invalidated and the user is signed out.

**5. Account Lockout**
SuperAdmin can manually lock accounts via `ToggleLockout`. Locked accounts cannot log in regardless of correct credentials.

### Proof of Restricted Pages

Attempting to access a role-restricted page without the correct role results in:
- HTTP 403 response
- Redirect to `/Home/AccessDenied`
- An audit log entry is not generated for the redirect itself, but the failed login or unauthorized access is visible in the browser's network tab as a 302 → 403 chain.

---

## 8. Code Auditing Tools

### Tools Used

| Tool | Type | How Applied |
|---|---|---|
| **Visual Studio Code Analysis** | Static analysis (built-in) | Runs on every build; flags nullable reference warnings, unused variables, unreachable code |
| **Roslyn Analyzers** (bundled with .NET SDK) | Static analysis | Enforces C# best practices, detects common security anti-patterns |
| **EF Core Model Validation** | Runtime validation | Validates model configuration on startup; catches misconfigured relationships and missing precision settings |
| **ASP.NET Core Startup Validation** | Runtime validation | Throws `InvalidOperationException` on missing required configuration (e.g., missing `Encryption:Key`, missing connection string) |
| **dotnet user-secrets** | Secrets management audit | Ensures secrets (encryption keys, OAuth credentials) are not stored in `appsettings.json` |

### Vulnerabilities Detected and Addressed

The following issues were identified during development and resolved:

| Vulnerability | Status | Resolution |
|---|---|---|
| Mass-assignment on `Expense.UserId` | Fixed | `[BindNever]` applied to `UserId`, `ReceiptData`, `ReceiptPath`, `ReceiptContentType` |
| CSRF on state-changing POST actions | Fixed | `[ValidateAntiForgeryToken]` applied to all POST actions |
| Brute force on login endpoint | Fixed | `LoginAttemptTracker` with 5-attempt lockout (5 min) and IP-level block (20 attempts, 15 min) |
| Sensitive tokens stored in plaintext | Fixed | AES-256 encryption via `EncryptionService` for `GmailRefreshToken` in all profile models |
| Secrets in source code / appsettings | Fixed | AWS, PayMongo, Gmail, and Encryption keys use environment variables / user secrets |
| Stack traces exposed in production | Fixed | `UseExceptionHandler("/Home/Error")` in production; developer page only in development |
| SQL injection | Mitigated | All database queries use EF Core parameterized queries; no raw SQL strings |
| Credential stuffing / account enumeration | Fixed | `SecurityThreatDetector` raises alerts and logs threats; IP blocking enforced |
| Horizontal privilege escalation | Fixed | Resource ownership checks (`r.UserId == userId`) in DriverController |

> Note: SonarLint, ESLint, and Bandit are not applicable to this C#/ASP.NET Core project. The equivalent static analysis is provided by Roslyn Analyzers and Visual Studio's built-in code analysis.

---

## 9. Testing

### Tests Conducted

Testing was performed across three categories: functional, security, and integration.

#### Functional Testing

| Test Case | Method | Expected Result |
|---|---|---|
| Driver submits valid expense report | Manual / Postman | Report saved, status = Submitted, notification sent to Manager |
| Driver submits report with missing required fields | Manual | Validation errors displayed; form not submitted |
| Manager approves a submitted report | Manual | Status changes to Approved; Driver notified |
| Manager rejects a report | Manual | Status changes to Rejected; Driver notified |
| CEO approves over-budget report | Manual | `CEOApproved = true`; Finance can process reimbursement |
| Finance marks report as reimbursed | Manual | `Reimbursed = true`; audit log entry created |
| SuperAdmin creates a user account | Manual | User created with correct role; profile record seeded |
| SuperAdmin locks a user account | Manual | User cannot log in; lockout reflected in UI |

#### Security Testing

| Test Case | Tool | Expected Result |
|---|---|---|
| Login with invalid credentials (5 attempts) | Manual / Browser | Account locked after 5th attempt; lockout message shown |
| Login with locked account | Manual | "Too many failed attempts. Please wait X seconds." |
| IP-level brute force (20+ failures) | Manual | IP blocked; "Too many failed attempts from your network." |
| CSRF attack simulation (POST without token) | Manual | HTTP 400 Bad Request; request rejected |
| Access Driver page as Manager | Manual / Browser | HTTP 403; redirected to Access Denied page |
| Access Manager page as unauthenticated user | Manual / Browser | Redirected to Login page |
| Driver accessing another driver's report by ID | Manual | HTTP 404 (ownership check fails) |
| Submit expense with `UserId` in POST body | Manual / Postman | `UserId` ignored (`[BindNever]`); server-side user ID used |

#### Integration Testing

| Test Case | Tool | Expected Result |
|---|---|---|
| Database migration on startup | Application startup | All migrations applied; no schema errors |
| Encryption/decryption of GmailRefreshToken | Manual | Token encrypted at save; decrypted correctly on read |
| Audit log written on login | Manual | `UserLogin` entry appears in AuditLogs table with correct IP and UserAgent |
| Threat detection on brute force | Manual | `BruteForceDetected` entry appears in AuditLogs with severity = Critical |
| PayMongo webhook signature validation | Postman | Valid signature accepted; invalid signature rejected |

### Tools Used

| Tool | Purpose |
|---|---|
| **Postman** | API endpoint testing (expense submission, webhook validation, receipt upload) |
| **Browser DevTools** | Network inspection, cookie inspection, CSRF token verification |
| **SQL Server Management Studio (SSMS)** | Direct database verification of audit logs, encrypted tokens, and report statuses |
| **Visual Studio Debugger** | Step-through debugging of validation and error handling logic |

---

## 10. Security Policies

### Password Policy

Configured in `Program.cs` via ASP.NET Core Identity options:

| Setting | Value | Notes |
|---|---|---|
| Minimum length | 6 characters | Enforced by Identity |
| Require digit | No | Disabled for usability |
| Require uppercase | No | Disabled for usability |
| Require lowercase | No | Disabled for usability |
| Require non-alphanumeric | No | Disabled for usability |
| Email confirmation required | No | `RequireConfirmedAccount = false` |

> Recommendation: For production hardening, enable `RequireDigit`, `RequireUppercase`, and increase `RequiredLength` to at least 8 characters.

Password reset uses ASP.NET Identity's secure token-based flow (`GeneratePasswordResetTokenAsync` / `ResetPasswordAsync`). Passwords are stored as bcrypt hashes managed by ASP.NET Core Identity — plaintext passwords are never stored.

### Login Attempt Policy

Implemented via `LoginAttemptTracker` (in-memory cache) and ASP.NET Identity lockout:

| Rule | Value |
|---|---|
| Max failed attempts per account | 5 |
| Account lockout duration | 5 minutes (300 seconds) |
| Max failed attempts per IP address | 20 |
| IP block duration | 15 minutes (900 seconds) |
| Lockout check | Before model validation (IP check) and before sign-in attempt (account check) |
| Attempt counter reset | On successful login |

After each failed attempt, the user is shown the remaining attempts count. After lockout, the remaining seconds are displayed. The lockout is enforced in-memory (fast, no DB round-trip for the check).

### Data Handling Policy

| Data Type | Handling |
|---|---|
| Passwords | Hashed with bcrypt via ASP.NET Core Identity; never stored in plaintext |
| Gmail OAuth Refresh Tokens | Encrypted with AES-256 before database storage; key stored in user secrets |
| Receipt files | Stored as binary in the database (`ReceiptData`) or in AWS S3 (`ReceiptPath`) depending on configuration |
| Audit logs | Retained in the database indefinitely; accessible only to SuperAdmin |
| Session cookies | `HttpOnly = true`, `IsEssential = true`; 30-minute idle timeout |
| Connection strings | Stored in `appsettings.json` using Windows Integrated Security (no username/password in connection string) |
| API keys (AWS, PayMongo, Gmail) | Stored in environment variables or user secrets; empty in `appsettings.json` |
| Encryption keys | Stored exclusively in `dotnet user-secrets`; never in `appsettings.json` |
| Database transport | SQL Server with `Encrypt=True` (TLS in transit) |

---

## 11. Incident Response Plan

### Detection

**Automated Detection:**
- The `SecurityThreatDetector` service continuously monitors the `AuditLogs` table for threat patterns after every login attempt.
- Threats are classified by severity (Low / Medium / High / Critical) and written to the audit log with action names like `BruteForceDetected`, `CredentialStuffingDetected`, `AccountEnumerationDetected`.
- The SuperAdmin dashboard displays recent audit log entries and can be filtered by action type to surface security events.

**Manual Detection:**
- SuperAdmin reviews the Audit Logs page (`/SuperAdmin/AuditLogs`) filtered by security-related actions.
- Unusual patterns (multiple `FailedLoginAttempt` entries from the same IP, `NewIpLogin` for privileged accounts) are visible in the log table.
- The threat summary endpoint (`SecurityThreatDetector.GetThreatSummaryAsync`) provides aggregated counts: failed logins in the last hour, unique attacker IPs, locked accounts, and recent threat events.

### Reporting

1. **Internal Reporting:**
   - SuperAdmin is notified via the audit log and dashboard.
   - For Critical/High severity threats, the SuperAdmin should immediately review the `AuditLogs` table filtered by the affected IP or user.

2. **Escalation Path:**
   - SuperAdmin → System Owner / IT Manager → (if data breach) affected users and relevant authorities.
   - If personal data is compromised, report to the Data Privacy Officer within 72 hours per applicable data protection regulations.

3. **Evidence Preservation:**
   - Export the relevant audit log entries to PDF from the SuperAdmin Audit Logs page before taking any remediation action.
   - Record the IP addresses, timestamps, and affected user IDs from the log entries.

### Containment

**Immediate Actions:**

| Threat | Containment Action | How |
|---|---|---|
| Compromised user account | Lock the account | SuperAdmin → User Management → Toggle Lockout |
| Ongoing brute force from an IP | IP is auto-blocked for 15 min by `LoginAttemptTracker`; for persistent attacks, block at firewall/network level | Network/hosting firewall rule |
| Compromised OAuth token (Gmail) | Disconnect Gmail integration | Profile page → Disconnect Gmail (triggers token deletion and audit log) |
| Compromised encryption key | Rotate the key in user secrets; re-encrypt all stored tokens | Update `Encryption:Key` and `Encryption:IV` in user secrets; run re-encryption script |
| Compromised API key (AWS/PayMongo) | Rotate the key in the provider's dashboard; update environment variable | AWS IAM / PayMongo dashboard → generate new key → update config |
| Suspicious admin activity | Review audit log; remove role or delete account if confirmed | SuperAdmin → User Management → Remove Role / Delete Account |

### Recovery

1. **Account Recovery:**
   - Unlock affected accounts via SuperAdmin → User Management → Toggle Lockout.
   - Force a password reset via the Forgot Password flow or by generating a reset token in SuperAdmin.

2. **Data Recovery:**
   - Use the built-in database backup/restore functionality (`/SuperAdmin/Backup`) to restore from the most recent clean backup.
   - Backup creation and restoration are both logged in the audit trail (`CreateBackup`, `RestoreBackup` actions).

3. **System Recovery:**
   - If the application is compromised at the infrastructure level, redeploy from source control.
   - Rotate all secrets (encryption keys, API keys, OAuth credentials) before redeployment.
   - Apply any pending database migrations after restore.

4. **Post-Incident Review:**
   - Export the full audit log for the incident period.
   - Identify the root cause (weak password, exposed secret, unpatched vulnerability).
   - Update security policies, thresholds, or code as needed.
   - Document the incident, timeline, and resolution for future reference.

---

*Document generated from CEMS codebase analysis — May 2026.*
