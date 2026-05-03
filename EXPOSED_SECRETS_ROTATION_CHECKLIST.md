# 🚨 CRITICAL: Exposed Secrets - Immediate Action Required

**Status**: 🔴 **EXPOSED SECRETS FOUND IN CODE REPOSITORY**

> ⚠️ If this repository is public or accessible to others, these secrets are **COMPROMISED** and must be rotated immediately.

---

## 📋 Exposed Secrets Checklist

### Database Credentials - 🔴 EXPOSED
- **Server**: `db41767.public.databaseasp.net`
- **Database**: `db41767`
- **User ID**: `db41767`
- **Password**: `[REDACTED]`
- **Status**: ⏳ **MUST ROTATE**
- **Action**:
  - [ ] Change database password immediately
  - [ ] Generate new connection string
  - [ ] Update User Secrets locally
  - [ ] Update environment variables on server
  - [ ] Verify application reconnects successfully

### AWS S3 Credentials - 🔴 EXPOSED
- **Access Key**: `[REDACTED - rotate immediately]`
- **Secret Key**: `[REDACTED - rotate immediately]`
- **Bucket**: `cems13`
- **Region**: `ap-southeast-1`
- **Status**: ⏳ **MUST ROTATE**
- **Action**:
  - [ ] Delete current IAM credentials in AWS Console
  - [ ] Create new access key/secret key pair
  - [ ] Update User Secrets: `AWS:AccessKey`, `AWS:SecretKey`
  - [ ] Update environment variables on server
  - [ ] Test S3 upload functionality
  - [ ] Monitor AWS logs for unauthorized access attempts

### Gmail OAuth Credentials - 🔴 EXPOSED
- **Client ID**: `[REDACTED - rotate immediately]`
- **Client Secret**: `[REDACTED - rotate immediately]`
- **Redirect URI**: `http://localhost:62449/profile/gmail-callback`
- **Status**: ⏳ **MUST ROTATE**
- **Action**:
  - [ ] Go to [Google Cloud Console](https://console.cloud.google.com)
  - [ ] Delete current OAuth credentials
  - [ ] Create new OAuth 2.0 Client ID
  - [ ] Update User Secrets: `Gmail:ClientId`, `Gmail:ClientSecret`
  - [ ] Update environment variables on server
  - [ ] Update redirect URI to production domain
  - [ ] Test Gmail login flow
  - [ ] Check Google activity for unauthorized logins

### PayMongo API Key - 🔴 EXPOSED
- **Secret Key**: `[REDACTED]`
- **Status**: ⏳ **MUST ROTATE**
- **Action**:
  - [ ] Log into [PayMongo Dashboard](https://dashboard.paymongo.com)
  - [ ] Navigate to API Keys section
  - [ ] Revoke current test key
  - [ ] Generate new test/live key pair
  - [ ] Update User Secrets: `PayMongo:SecretKey`
  - [ ] Update environment variables on server
  - [ ] Update User Secrets: `PayMongo:WebhookSecret` if needed
  - [ ] Test payment processing with new key
  - [ ] Monitor PayMongo logs for unauthorized transactions

### Hardcoded Seeder Password - 🟠 EXPOSED (Code Only)
- **Password**: `P@ssw0rd123`
- **Location**: `Data/SeedData.cs` (now fixed)
- **Status**: ✅ **FIXED IN CODE**
- **Action**:
  - [ ] Already fixed - uses configuration instead
  - [ ] Rotate all test account passwords created with this
  - [ ] Users to check: superadmin@, ceo@, manager@, driver@, finance@expense.com
  - [ ] Force password reset on next login

---

## 🔐 Rotation Priority

### Phase 1: IMMEDIATE (Today)
1. [ ] Delete AWS credentials
2. [ ] Delete/revoke Gmail OAuth credentials
3. [ ] Revoke PayMongo key
4. [ ] Monitor for unauthorized access

### Phase 2: URGENT (Within 24 hours)
1. [ ] Generate new AWS credentials
2. [ ] Generate new Gmail OAuth credentials
3. [ ] Generate new PayMongo key
4. [ ] Update User Secrets locally
5. [ ] Test all integrations

### Phase 3: PRODUCTION (Within 48 hours)
1. [ ] Update environment variables on production server
2. [ ] Redeploy application with new credentials
3. [ ] Verify all integrations working
4. [ ] Update documentation with new URLs/IDs

---

## 🛡️ How to Rotate Credentials

### AWS S3
```
1. AWS Console → IAM → Users → Find user with this access key
2. Security credentials → Access keys → Delete old key
3. Create access key → Copy new credentials
4. dotnet user-secrets set "AWS:AccessKey" "NEW_KEY"
5. dotnet user-secrets set "AWS:SecretKey" "NEW_SECRET"
6. Test: Upload file to S3
```

### Google OAuth
```
1. Google Cloud Console → APIs & Services → Credentials
2. Find OAuth 2.0 Client ID → Delete
3. Create New → OAuth 2.0 Client ID
4. Set Authorized redirect URIs to your production domain
5. Download JSON → Copy credentials
6. dotnet user-secrets set "Gmail:ClientId" "NEW_ID"
7. dotnet user-secrets set "Gmail:ClientSecret" "NEW_SECRET"
8. Test: Gmail login flow
```

### PayMongo
```
1. PayMongo Dashboard → Settings → API Keys
2. Test keys → Revoke (if test, can skip)
3. Live keys → Revoke current → Generate new
4. dotnet user-secrets set "PayMongo:SecretKey" "NEW_KEY"
5. Test: Create test payment
```

### Database
```
1. SQL Server Management Studio → Security → Logins
2. Right-click user → Properties → Change password
3. New connection string: "...;Password=NewSecurePassword;..."
4. dotnet user-secrets set "ConnectionStrings:DefaultConnection" "NEW_CONNECTION_STRING"
5. Test: Verify database connection in app
```

---

## 🔍 Verification Steps

After rotating each secret:

```bash
# 1. Test locally with User Secrets
dotnet run

# Check logs for:
# ✅ Database connected successfully
# ✅ Gmail OAuth endpoint reachable
# ✅ PayMongo API responding
# ✅ S3 bucket accessible

# 2. Test specific features
# - [ ] Create account (DB write)
# - [ ] Login with Gmail (OAuth)
# - [ ] Upload receipt (S3)
# - [ ] Process payment (PayMongo)
```

---

## 📊 Exposure Assessment

| Secret | Exposed In | Where Visible | Risk Level | Rotation |
|--------|-----------|---------------|-----------|----------|
| DB Password | `appsettings.json` | GitHub, backups, clones | 🔴 CRITICAL | Today |
| AWS AccessKey | `appsettings.json` + dev | GitHub, backups, clones | 🔴 CRITICAL | Today |
| AWS SecretKey | `appsettings.json` + dev | GitHub, backups, clones | 🔴 CRITICAL | Today |
| Gmail ClientID | `appsettings.json` + guide | GitHub, documentation | 🟠 HIGH | Today |
| Gmail Secret | `appsettings.json` | GitHub, backups, clones | 🔴 CRITICAL | Today |
| PayMongo Key | `appsettings.Dev.json` | GitHub, backups, clones | 🔴 CRITICAL | Today |
| Seeder Password | `SeedData.cs` | GitHub only (low risk) | 🟡 MEDIUM | Within 24h |

---

## ✅ Remediation Complete When

- [ ] All secrets removed from configuration files
- [ ] SeedData no longer has hardcoded password
- [ ] .gitignore prevents future secret commits
- [ ] AWS credentials rotated and new ones set
- [ ] Gmail OAuth credentials rotated and new ones set
- [ ] PayMongo key rotated and new one set
- [ ] Database password changed and new one set
- [ ] All User Secrets configured locally
- [ ] Application tested with new credentials
- [ ] Environment variables updated on production
- [ ] Production application redeployed and verified
- [ ] No more errors related to missing credentials
- [ ] All integrations working (Gmail, S3, PayMongo)

---

## 🚨 If Repository is Public

If this repository was pushed to GitHub or is publicly accessible:

1. **IMMEDIATELY** rotate all credentials (see above)
2. Notify stakeholders of potential exposure
3. Monitor accounts for unauthorized activity:
   - Database: Check connection logs
   - AWS: Check CloudTrail logs
   - Gmail: Check Google account activity
   - PayMongo: Check transaction logs
4. Consider removing sensitive commits from history:
   ```bash
   git-filter-branch --tree-filter 'rm -f appsettings.json' HEAD
   git push origin --force --all
   ```
5. Make repository private if not already

---

## 📞 Emergency Contacts

- **Database Admin**: [Your DBA contact]
- **AWS Account Owner**: [Your AWS contact]
- **Security Team**: [Your security contact]
- **Payment Processor**: PayMongo support

---

## 📝 Notes

- These secrets were **NOT** in the original repo structure provided
- All secrets have been **REMOVED** from configuration files
- Application now uses **User Secrets** (dev) and **Environment Variables** (prod)
- See `SECRETS_MANAGEMENT.md` for setup instructions
- See `SECURITY_VULNERABILITY_REPORT.md` for full assessment

---

**Status**: 🔴 **ACTION REQUIRED**  
**Deadline**: Today (within business hours)  
**Severity**: 🔴 CRITICAL

> ⚠️ Do not deploy this application to production until all credentials are rotated and properly configured via User Secrets or environment variables.
