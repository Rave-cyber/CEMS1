# CEMS - Security Screenshots and Verification Guide

**Date**: May 3, 2026

## How to Capture Security Screenshots

This guide provides step-by-step instructions to capture screenshots demonstrating security features for your CEMS documentation.

---

## 1. Authentication & Registration Screenshots

### Screenshot 1.1: Registration Page
**Path**: `http://localhost:7001/Identity/Account/Register`

**Steps to Capture**:
1. Navigate to the registration page
2. Show the form with all validation fields
3. Highlight the email format requirement and password fields

**Key Elements to Show**:
- Email input with validation indicator
- Password field with minimum length requirement
- Password confirmation field
- Submit button

### Screenshot 1.2: Login Validation (Invalid Email)
**Path**: `http://localhost:7001/Identity/Account/Login`

**Steps to Capture**:
1. Enter invalid email format (e.g., "invalid.email")
2. Take screenshot showing validation error
3. Show error message: "The Email field is not a valid e-mail address."

**Expected Result**:
```
❌ Email validation error displayed
❌ Form submission prevented
```

### Screenshot 1.3: Password Reset Flow
**Path**: `http://localhost:7001/Identity/Account/ForgotPassword`

**Steps to Capture**:
1. Enter email address
2. Submit form
3. Capture confirmation message

**Expected Message**:
```
"Please check your email to reset your password."
```

---

## 2. Role-Based Access Control Screenshots

### Screenshot 2.1: Unauthorized Access Attempt
**Path**: Access `/CEO/Dashboard` as a Driver

**Steps to Capture**:
1. Login as a Driver account
2. Attempt to navigate to `/CEO/Dashboard`
3. Take screenshot of access denied or redirect to login

**Expected Result**:
```
HTTP 403 - Forbidden
OR
Redirect to: /Home/AccessDenied
```

### Screenshot 2.2: Role-Based Dashboard Access
**Multiple Dashboards**

**Driver Dashboard**:
- URL: `http://localhost:7001/Driver/Dashboard`
- Shows: Driver-specific expenses and reports
- Screenshot: Recent expenses list with Driver-only actions

**CEO Dashboard**:
- URL: `http://localhost:7001/CEO/Dashboard`
- Shows: Company-wide analytics and approval queue
- Screenshot: System overview and analytics

**Finance Dashboard**:
- URL: `http://localhost:7001/Finance/Dashboard`
- Shows: Payment processing and reimbursement controls
- Screenshot: Pending payments and reimbursement status

### Screenshot 2.3: Access Control Summary
**Navigate Through All Dashboards**

**Steps to Capture**:
1. Login as CEO
2. Navigate to all accessible dashboards
3. Try to access SuperAdmin section
4. Document which pages are accessible

**Documentation Template**:
```
ROLE: CEO
✅ Accessible: /CEO/Dashboard, /Manager/Dashboard
✅ Can View: Driver reports (read-only)
❌ Cannot Access: /SuperAdmin/Users
```

---

## 3. Input Validation Screenshots

### Screenshot 3.1: Invalid Email Format
**Location**: Any registration/login form

**Steps to Capture**:
1. Enter email without @ symbol: "testuser"
2. Tab out or submit form
3. Capture validation error message

**Expected Error**:
```
"The Email field is not a valid e-mail address."
```

### Screenshot 3.2: Password Length Validation
**Location**: Registration form

**Steps to Capture**:
1. Enter password with less than 6 characters: "123"
2. Press Tab or try to submit
3. Capture validation error

**Expected Error**:
```
"The Password must be at least 6 and at max 100 characters long."
```

### Screenshot 3.3: Password Confirmation Mismatch
**Location**: Registration form

**Steps to Capture**:
1. Password field: "TestPass123"
2. Confirm password: "TestPass456"
3. Try to submit
4. Capture error message

**Expected Error**:
```
"The password and confirmation password do not match."
```

### Screenshot 3.4: Expense Form Validation
**Location**: `/Driver/Submit` page

**Steps to Capture**:
1. Leave required fields empty
2. Try to submit form
3. Capture validation messages for:
   - Missing category
   - Missing amount
   - Invalid amount (negative, zero)

**Expected Errors**:
```
"Category is required"
"Amount is required"
"Amount must be greater than 0"
```

### Screenshot 3.5: Character Limit Validation
**Location**: Profile editing or any text field

**Steps to Capture**:
1. Find a field with MaxLength attribute (e.g., City: 100 chars)
2. Paste very long text
3. Show character counter if available
4. Try to submit with oversized input

**Expected Result**:
```
Form submission prevented
Error: "City cannot exceed 100 characters"
```

---

## 4. Login Attempt Lockout Screenshots

### Screenshot 4.1: Failed Login Attempt 1
**Path**: `http://localhost:7001/Identity/Account/Login`

**Steps to Capture**:
1. Enter valid email
2. Enter wrong password (Attempt 1 of 3)
3. Click Sign In
4. Capture: "Invalid email or password" message
5. Capture displayed Failed Attempts counter

**Display**:
```
Failed Attempts: 1/3
Remaining Attempts: 2
```

### Screenshot 4.2: Failed Login Attempt 2
**Path**: Same login page

**Steps to Capture**:
1. Enter wrong password again (Attempt 2 of 3)
2. Capture the updated counter

**Display**:
```
Failed Attempts: 2/3
Remaining Attempts: 1
⚠️ WARNING: One more attempt will lock your account
```

### Screenshot 4.3: Account Lockout (After 3 Failed Attempts)
**Path**: Same login page

**Steps to Capture**:
1. Enter wrong password third time
2. Capture lockout message
3. Note the lockout duration

**Expected Message**:
```
❌ Too many failed attempts. Please wait 30 seconds before trying again.
Remaining lockout time: 00:30
```

### Screenshot 4.4: Lockout Timer
**Path**: Same login page (wait during lockout)

**Steps to Capture**:
1. Stay on login page during 30-second lockout
2. Capture countdown timer (if shown)
3. Wait 30 seconds
4. Capture successful login with correct credentials

**Timeline**:
```
00:30 - Initial lockout
00:25 - Counting down
00:05 - Almost expired
00:00 - Lockout expired, can login
```

---

## 5. Security Headers Screenshots

### Screenshot 5.1: HTTPS Enforcement
**Browser Address Bar**

**Steps to Capture**:
1. Navigate to any page
2. Show the padlock icon in address bar
3. Click padlock to show security details
4. Capture certificate information

**Display**:
```
🔒 Secure (HTTPS)
TLS 1.3
Certificate: Valid
Issuer: Self-signed or valid CA
```

### Screenshot 5.2: Security Headers (Browser DevTools)
**Steps to Capture**:
1. Open page in Chrome/Edge
2. Press F12 to open DevTools
3. Go to Network tab
4. Reload page
5. Click any request
6. Navigate to Response Headers
7. Show security-related headers:

**Expected Headers**:
```
Strict-Transport-Security: max-age=31536000
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Set-Cookie: [secure flags]
```

### Screenshot 5.3: Cookie Security Flags
**Browser DevTools > Application/Storage**

**Steps to Capture**:
1. Open DevTools
2. Go to Application tab
3. Navigate to Cookies
4. Select the application cookie
5. Capture cookie properties

**Properties to Show**:
```
Name: .AspNetCore.Identity.Application
Domain: localhost
Path: /
Expires: [Session end time]
HttpOnly: ✅ Yes
Secure: ✅ Yes
SameSite: Strict/Lax
```

---

## 6. Database & Data Encryption Screenshots

### Screenshot 6.1: User Secrets File Location
**File System**

**Steps to Capture**:
1. Open File Explorer
2. Navigate to: `%APPDATA%\Microsoft\UserSecrets\`
3. Find the project GUID folder
4. Show `secrets.json` file (encrypted)

**Path**:
```
C:\Users\[YourUsername]\AppData\Roaming\Microsoft\UserSecrets\[ProjectGUID]\secrets.json
```

**Display**:
```
📁 UserSecrets
  📁 [Project GUID]
    📄 secrets.json (encrypted file)
```

### Screenshot 6.2: Secrets Content Structure
**VS Code/Text Editor**

**Steps to Capture**:
1. Open secrets.json in editor
2. Show the structure (without values)
3. Highlight sensitive keys

**Display**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "[ENCRYPTED]"
  },
  "AWS": {
    "AccessKey": "[ENCRYPTED]",
    "SecretKey": "[ENCRYPTED]"
  },
  "Gmail": {
    "ClientId": "[ENCRYPTED]",
    "ClientSecret": "[ENCRYPTED]"
  },
  "PayMongo": {
    "SecretKey": "[ENCRYPTED]"
  }
}
```

### Screenshot 6.3: Database Encryption in SQL Server
**SQL Server Management Studio**

**Steps to Capture**:
1. Connect to SQL Server database
2. Navigate to Databases > CEMS > Tables
3. Find DriverProfiles table
4. Right-click > Design
5. Show GmailRefreshToken column properties

**Display**:
```
Column Name: GmailRefreshToken
Data Type: nvarchar(500)
Nullable: Yes
Encrypted: ✅ Yes (EF Core Data Protection)
```

---

## 7. Audit Logging Screenshots

### Screenshot 7.1: Audit Log Table
**SQL Server / Database**

**Steps to Capture**:
1. Open SQL Server Management Studio
2. Query: `SELECT TOP 100 * FROM AuditLogs ORDER BY Timestamp DESC`
3. Show results with:

**Display Columns**:
```
Id | Action | Module | Role | PerformedByUserId | TargetUserId | Details | Timestamp
1  | Login  | Auth   | Driver | user123 | NULL | Successful login | 2026-05-03 14:23:15
2  | Submit | Expense| Driver | user123 | NULL | Submitted report #42 | 2026-05-03 14:25:30
3  | Approve| Report | CEO | user456 | user123 | Approved report #42 | 2026-05-03 14:26:00
```

### Screenshot 7.2: Application Logging Output
**Visual Studio Debug Window / Console**

**Steps to Capture**:
1. Run application in Debug mode
2. Perform actions (login, submit expense, etc.)
3. Show Output window with log messages
4. Capture log entries

**Display**:
```
[14:23:15] INFO - HomeController: User logged in
[14:23:30] INFO - DriverController: Expense submitted successfully
[14:23:45] INFO - PayMongoService: Payment request created
[14:24:00] ERROR - S3StorageService: Failed to upload receipt to S3
```

---

## 8. Security Scan Results Screenshots

### Screenshot 8.1: Security Code Scan Warnings
**Visual Studio Error List**

**Steps to Capture**:
1. Open Visual Studio
2. Build project (Ctrl+Shift+B)
3. Open Error List window (View > Error List)
4. Show warnings related to security

**Display**:
```
Warning | SCS0016: Controller method is potentially vulnerable to CSRF
Warning | SCS0005: Weak random number generator  
Warning | SCS0027: Potential Open Redirect vulnerability
```

### Screenshot 8.2: Security Code Scan Details
**Error Details Pane**

**Steps to Capture**:
1. Click on each warning in Error List
2. Show the detailed message
3. Capture the file location and line number

**Display**:
```
File: DriverController.cs
Line: 594
Message: Controller method is potentially vulnerable to Cross Site 
Request Forgery (CSRF)
Link: https://security-code-scan.github.io/#SCS0016
```

---

## 9. Browser Security Testing Screenshots

### Screenshot 9.1: Mixed Content Detection
**Browser Console**

**Steps to Capture**:
1. Press F12 to open DevTools
2. Go to Console tab
3. Look for any mixed content warnings
4. Show result (should show none)

**Expected**:
```
✅ No warnings about mixed content
✅ No HTTP requests on HTTPS page
```

### Screenshot 9.2: Cross-Origin Request Blocking
**Browser Network Tab**

**Steps to Capture**:
1. Open DevTools > Network tab
2. Attempt a cross-origin request from browser console:
   ```javascript
   fetch('https://other-domain.com/api')
   ```
3. Capture the CORS error

**Display**:
```
🚫 Access to XMLHttpRequest at 'https://other-domain.com/api' 
from origin 'https://localhost:7001' has been blocked by CORS policy
```

---

## 10. Video Demonstration Script

### Demo Scenario: Complete Security Walkthrough

**Duration**: 5-10 minutes

**Steps**:

1. **Introduction (30 seconds)**
   - Show application homepage
   - Explain security measures implemented

2. **Authentication Demo (2 minutes)**
   - Show login page with validation
   - Demonstrate failed login attempts
   - Show account lockout feature
   - Successful login with valid credentials

3. **Authorization Demo (2 minutes)**
   - Login as different role (Driver)
   - Show driver dashboard
   - Try to access CEO dashboard
   - Show "Access Denied" message
   - Show available/restricted areas

4. **Input Validation Demo (2 minutes)**
   - Show registration form validation
   - Invalid email format rejection
   - Password mismatch detection
   - Expense submission with invalid data

5. **Security Headers Demo (1 minute)**
   - Open DevTools
   - Show HTTPS/TLS in address bar
   - Show security headers
   - Show secure cookie flags

6. **Summary (1 minute)**
   - Recap security features
   - Show documentation reference

---

## Verification Checklist

Use this checklist to verify all security features are working:

### Authentication ✓
- [ ] Registration requires valid email
- [ ] Registration requires matching passwords
- [ ] Login requires valid credentials
- [ ] Failed login shows error message
- [ ] Account locks after 3 failed attempts
- [ ] Lockout expires after 30 seconds
- [ ] Session timeout works (30 minutes)

### Authorization ✓
- [ ] Driver cannot access CEO dashboard
- [ ] Manager can access reports
- [ ] Finance can process payments
- [ ] SuperAdmin has full access
- [ ] Non-authenticated users redirected to login

### Input Validation ✓
- [ ] Email format validated
- [ ] Password length validated (min 6)
- [ ] Required fields enforced
- [ ] Text length limits respected
- [ ] Amount validation (positive numbers only)
- [ ] Special characters handled correctly

### Data Protection ✓
- [ ] HTTPS enforced (no HTTP)
- [ ] SSL/TLS certificate valid
- [ ] Secure cookies set
- [ ] User secrets encrypted
- [ ] Database connection encrypted
- [ ] Passwords hashed with PBKDF2

### Error Handling ✓
- [ ] Generic errors shown to users
- [ ] Detailed errors in logs only
- [ ] No sensitive info in error messages
- [ ] Proper HTTP status codes returned
- [ ] Audit log entries created

---

## Summary of Security Implementation

### Visual Security Flow Diagram

```
User Request
    ↓
[HTTPS/TLS] - Encrypted in Transit
    ↓
[Authentication] - Username/Password or OAuth
    ↓
[Authorization] - Role-based access check
    ↓
[Input Validation] - Data annotations
    ↓
[Business Logic] - Process request
    ↓
[Audit Logging] - Log action
    ↓
[Error Handling] - Safe error response
    ↓
[Response] - HTTPS encrypted response
```

### Security Layers

```
Layer 1: Transport         🔒 HTTPS/TLS 1.3
Layer 2: Authentication   🔐 Identity + OAuth
Layer 3: Authorization    👤 Role-based (RBAC)
Layer 4: Input Validation ✓ Data Annotations
Layer 5: Data Protection  🔑 Encryption + Hashing
Layer 6: Audit Trail      📋 Logging
Layer 7: Error Handling   ⚠️ Safe Error Messages
```

---

**Document Version**: 1.0  
**Created**: May 3, 2026  
**Purpose**: Guide for capturing security verification screenshots
