# 🔐 Secrets Management Guide

## Overview
This application uses secure methods to manage sensitive data (API keys, database passwords, etc.) without exposing them in version control.

## For Development

### Step 1: Initialize User Secrets

```bash
cd c:\Users\Lenovo\source\repos\CEMS
dotnet user-secrets init
```

### Step 2: Set Your Development Secrets

```bash
# Database Connection
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER; Database=CEMS; User Id=YOUR_USER; Password=YOUR_PASSWORD; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;"

# AWS Credentials
dotnet user-secrets set "AWS:AccessKey" "YOUR_AWS_ACCESS_KEY"
dotnet user-secrets set "AWS:SecretKey" "YOUR_AWS_SECRET_KEY"

# Gmail OAuth (from Google Cloud Console)
dotnet user-secrets set "Gmail:ClientId" "YOUR_GMAIL_CLIENT_ID"
dotnet user-secrets set "Gmail:ClientSecret" "YOUR_GMAIL_CLIENT_SECRET"

# PayMongo Test Keys (from PayMongo Dashboard)
dotnet user-secrets set "PayMongo:SecretKey" "YOUR_PAYMONGO_SECRET_KEY"
dotnet user-secrets set "PayMongo:WebhookSecret" "YOUR_PAYMONGO_WEBHOOK_SECRET"

# Seeder Temporary Password
dotnet user-secrets set "Seeder:TempPassword" "TempP@ss123!Secure"
```

### Step 3: Verify Secrets Are Set

```bash
dotnet user-secrets list
```

## For Production

### Environment Variables

Set these as environment variables on your production server:

```powershell
# Windows
$env:ConnectionStrings__DefaultConnection = "Server=PROD_SERVER;Database=CEMS;..."
$env:AWS__AccessKey = "YOUR_PROD_AWS_KEY"
$env:AWS__SecretKey = "YOUR_PROD_AWS_SECRET"
$env:Gmail__ClientId = "YOUR_PROD_GMAIL_ID"
$env:Gmail__ClientSecret = "YOUR_PROD_GMAIL_SECRET"
$env:PayMongo__SecretKey = "sk_live_YOUR_PROD_KEY"
$env:PayMongo__WebhookSecret = "YOUR_WEBHOOK_SECRET"
```

**Note:** Use double underscores `__` for nested configuration in environment variables.

### Or use appsettings.Production.json with environment variable substitution

## Secrets Stored

| Key | Purpose | Example |
|-----|---------|---------|
| `ConnectionStrings:DefaultConnection` | Database connection | `Server=...;Password=...` |
| `AWS:AccessKey` | AWS S3 access | `AKIA...` |
| `AWS:SecretKey` | AWS S3 secret | `vXZa7c...` |
| `Gmail:ClientId` | Google OAuth client | `295664932...apps.googleusercontent.com` |
| `Gmail:ClientSecret` | Google OAuth secret | `[REDACTED]...` |
| `PayMongo:SecretKey` | Payment gateway key | `sk_test_...` or `sk_live_...` |
| `PayMongo:WebhookSecret` | Webhook verification | `whsec_...` |
| `Seeder:TempPassword` | Temp seed user password | `TempP@ss123!Secure` |

## How It Works

1. **Development**: User Secrets stored in `%APPDATA%\Microsoft\UserSecrets\CEMS\secrets.json` (encrypted, local machine only)
2. **appsettings.json**: Contains empty strings for all secrets (safe for version control)
3. **appsettings.Development.json**: Overrides with empty strings (merges with User Secrets)
4. **appsettings.Production.json**: Empty values, relies on environment variables
5. **Runtime**: Configuration system automatically merges sources in order

## Security Best Practices

✅ **DO:**
- Store all API keys in User Secrets (development)
- Use environment variables in production
- Rotate keys regularly
- Use different keys for dev/test/production
- Use strong temporary seeder passwords
- Add `secrets.json` patterns to `.gitignore`

❌ **DON'T:**
- Commit secrets to version control
- Hardcode passwords in code
- Use the same key for multiple environments
- Share keys via email or chat
- Use weak passwords for seeding

## Gitignore Entry

Ensure `.gitignore` contains:
```
secrets.json
*.user
*.local
User Secrets/
```

## Testing

```bash
# These should return empty or null
dotnet user-secrets set "ConnectionStrings:DefaultConnection" --project .
dotnet user-secrets list

# This should show "Key not found" errors if not set
dotnet run
```

## Troubleshooting

**Secrets not loading?**
- Check `.csproj` has `UserSecretsId`: `<UserSecretsId>aspnet-CEMS-{guid}</UserSecretsId>`
- Run `dotnet user-secrets init` again
- Verify path: `%APPDATA%\Microsoft\UserSecrets\`

**Environment variables not working?**
- Use `__` for nested config (e.g., `AWS__AccessKey`)
- Restart application after setting variables
- Check variable is actually set: `echo %AWS__AccessKey%`

**Seeder failing?**
- Set `Seeder:TempPassword` in User Secrets
- Verify password meets complexity requirements
- Check if users already exist (they won't be recreated)
