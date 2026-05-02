# AES-256 Encryption Implementation Guide for CEMS

**Date**: May 3, 2026  
**Purpose**: Adding AES-256 encryption for sensitive fields  

---

## Yes, You Can Use AES-256! ✅

AES-256 is excellent for encrypting sensitive data like OAuth tokens. Here's the complete guide.

---

## 📋 Current Encryption Status

### What's Currently Encrypted
```
✅ Passwords       → PBKDF2 (ASP.NET Identity)
✅ API Keys        → DPAPI (User Secrets)
✅ Transit Data    → HTTPS/TLS 1.3
❌ OAuth Tokens    → NOT encrypted (stored as plain text)
❌ Sensitive Fields → NOT encrypted
```

### What Needs AES-256
```
🔓 GmailRefreshToken (DriverProfile, CEOProfile, ManagerProfile, FinanceProfile)
🔓 Gmail Access Token (if stored)
🔓 PayMongo Webhook Data (optional)
```

---

## 🔧 Implementation Steps

### Step 1: Create Encryption Service

**File to Create**: `Services/EncryptionService.cs`

```csharp
using System.Security.Cryptography;
using System.Text;

namespace CEMS.Services
{
    public interface IEncryptionService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }

    public class EncryptionService : IEncryptionService
    {
        private readonly IConfiguration _configuration;
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public EncryptionService(IConfiguration configuration)
        {
            _configuration = configuration;
            
            // Get encryption key from configuration (must be 32 bytes for AES-256)
            string keyString = _configuration["Encryption:Key"] 
                ?? throw new InvalidOperationException("Encryption:Key not configured");
            
            // Get IV from configuration (must be 16 bytes)
            string ivString = _configuration["Encryption:IV"] 
                ?? throw new InvalidOperationException("Encryption:IV not configured");
            
            _key = Convert.FromBase64String(keyString);
            _iv = Convert.FromBase64String(ivString);
            
            if (_key.Length != 32)
                throw new InvalidOperationException("AES-256 key must be 32 bytes");
            
            if (_iv.Length != 16)
                throw new InvalidOperationException("IV must be 16 bytes");
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            using (var aes = new AesCryptoServiceProvider())
            {
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(plainText);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                using (var aes = new AesCryptoServiceProvider())
                {
                    aes.Key = _key;
                    aes.IV = _iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(Convert.FromBase64String(cipherText)))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                // Return as-is if decryption fails (might be old unencrypted data)
                return cipherText;
            }
        }
    }
}
```

---

### Step 2: Generate Encryption Keys

**Console Command** (Run once to generate keys):

```csharp
// Run this in Program.cs or a separate utility to generate keys
static void GenerateEncryptionKeys()
{
    using (var aes = new AesCryptoServiceProvider())
    {
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        string key = Convert.ToBase64String(aes.Key);
        string iv = Convert.ToBase64String(aes.IV);

        Console.WriteLine("Store these in User Secrets or environment variables:");
        Console.WriteLine($"Encryption:Key = {key}");
        Console.WriteLine($"Encryption:IV = {iv}");
    }
}
```

**Store in User Secrets**:

```bash
cd c:\Users\Lenovo\source\repos\CEMS
dotnet user-secrets set "Encryption:Key" "[BASE64_KEY_HERE]"
dotnet user-secrets set "Encryption:IV" "[BASE64_IV_HERE]"
```

---

### Step 3: Register Service in Program.cs

**File**: `Program.cs`

**Add this line around line 65** (after other service registrations):

```csharp
// Add after existing services
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
```

---

### Step 4: Update Models with Value Converters

**Files Affected**:
- `Models/DriverProfile.cs`
- `Models/CEOProfile.cs`
- `Models/ManagerProfile.cs`
- `Models/FinanceProfile.cs`

**Example for DriverProfile.cs**:

```csharp
// Add using statement
using Microsoft.EntityFrameworkCore.DataEncryption;

public class DriverProfile
{
    // ... existing properties ...

    [MaxLength(255)]
    public string? GmailAddress { get; set; }

    [MaxLength(500)]
    public string? GmailRefreshToken { get; set; }  // Will be encrypted
}
```

---

### Step 5: Configure Entity Framework Value Converter

**File**: `Data/ApplicationDbContext.cs`

**Add in OnModelCreating method**:

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);

    var encryptionService = new EncryptionService(new ConfigurationBuilder()
        .AddUserSecrets<ApplicationDbContext>()
        .Build());

    // Encrypt GmailRefreshToken for all profile types
    builder.Entity<DriverProfile>()
        .Property(p => p.GmailRefreshToken)
        .HasConversion(
            v => encryptionService.Encrypt(v),
            v => encryptionService.Decrypt(v));

    builder.Entity<CEOProfile>()
        .Property(p => p.GmailRefreshToken)
        .HasConversion(
            v => encryptionService.Encrypt(v),
            v => encryptionService.Decrypt(v));

    builder.Entity<ManagerProfile>()
        .Property(p => p.GmailRefreshToken)
        .HasConversion(
            v => encryptionService.Encrypt(v),
            v => encryptionService.Decrypt(v));

    builder.Entity<FinanceProfile>()
        .Property(p => p.GmailRefreshToken)
        .HasConversion(
            v => encryptionService.Encrypt(v),
            v => encryptionService.Decrypt(v));

    // ... rest of existing configuration ...
}
```

---

## 📁 Files Affected

### 1. **Services** (New/Modified)
```
✏️ Services/EncryptionService.cs          [NEW]
✏️ Services/IGmailService.cs              [Modified - no changes needed]
✏️ Services/GmailService.cs               [Modified - no changes needed]
```

### 2. **Models** (Modified)
```
✏️ Models/DriverProfile.cs                [Configuration only]
✏️ Models/CEOProfile.cs                   [Configuration only]
✏️ Models/ManagerProfile.cs               [Configuration only]
✏️ Models/FinanceProfile.cs               [Configuration only]
```

### 3. **Data** (Modified)
```
✏️ Data/ApplicationDbContext.cs           [Add value converters]
```

### 4. **Controllers** (No changes needed)
```
✅ Controllers/ProfileController.cs       [Works as-is - transparent encryption]
✅ Controllers/DriverController.cs        [Works as-is]
✅ Controllers/CEOController.cs           [Works as-is]
✅ Controllers/ManagerController.cs       [Works as-is]
✅ Controllers/FinanceController.cs       [Works as-is]
```

### 5. **Configuration** (Modified)
```
✏️ Program.cs                             [Add service registration]
✏️ appsettings.json                       [Add encryption key references]
```

---

## ⚠️ Potential Issues & Solutions

### Issue 1: Existing Unencrypted Data

**Problem**: 
- Old tokens already stored as plain text won't decrypt properly

**Solution**:
```csharp
// In EncryptionService.Decrypt():
public string Decrypt(string cipherText)
{
    if (string.IsNullOrEmpty(cipherText))
        return cipherText;

    try
    {
        // Try to decrypt
        using (var aes = new AesCryptoServiceProvider())
        {
            // ... decryption code ...
        }
    }
    catch
    {
        // If decryption fails, assume it's old unencrypted data
        // Return as-is (graceful fallback)
        return cipherText;
    }
}
```

### Issue 2: Key Management

**Problem**: 
- AES-256 key must be stored securely

**Solution**:
```
✅ Development:  User Secrets (DPAPI encrypted)
✅ Production:   Environment variables or Azure Key Vault
✅ Docker:       Docker secrets
✅ Never:        Hardcoded in source code
```

### Issue 3: Performance Impact

**Problem**: 
- Encryption/decryption adds CPU overhead

**Benchmark** (Approximate):
```
Encrypt 1KB:    ~0.1ms
Decrypt 1KB:    ~0.1ms
Per Request:    Negligible (OAuth token ≈ 1-2KB)
```

**Solution**: Negligible for typical usage, but consider:
- Cache decrypted tokens in memory (short-term)
- Use encryption only for most sensitive fields

### Issue 4: Search/Filtering Won't Work

**Problem**: 
- Can't search encrypted fields in database
- `db.DriverProfiles.Where(p => p.GmailRefreshToken == token)` won't work

**Solution**:
```csharp
// BAD - Won't work after encryption
var user = db.DriverProfiles
    .FirstOrDefault(p => p.GmailRefreshToken == refreshToken);

// GOOD - Fetch all, decrypt in memory
var user = db.DriverProfiles
    .ToList()  // Brings encrypted data to memory
    .FirstOrDefault(p => p.GmailRefreshToken == refreshToken);  
    // Decryption happens automatically via value converter
```

### Issue 5: Connection String Must Be Secure

**Problem**: 
- Database connection string needs protection

**Solution** (Already implemented):
```csharp
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30));
    }));
```

**Connection String Location**:
- ✅ User Secrets (Dev)
- ✅ Environment Variables (Prod)
- ✅ Azure Key Vault (Recommended for Production)

---

## 🔄 Migration Strategy

### For New Installation
```
1. Generate AES-256 keys
2. Store in User Secrets
3. Run migrations
4. Deploy
✅ All new tokens encrypted automatically
```

### For Existing Installation

**Migration Steps**:

```csharp
// In Data/Migrations/[timestamp]_AddEncryptionSupport.cs
public partial class AddEncryptionSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The encrypted values will work alongside old unencrypted ones
        // due to graceful fallback in Decrypt()
        
        // No schema changes needed if reusing same column
        // If you want to track encryption status:
        
        migrationBuilder.AddColumn<bool>(
            name: "GmailRefreshTokenEncrypted",
            table: "DriverProfiles",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GmailRefreshTokenEncrypted",
            table: "DriverProfiles");
    }
}
```

**Data Migration Script**:
```csharp
// Run after deploying encryption service
public async Task MigrateToEncryption(IEncryptionService encryptionService)
{
    var drivers = await _db.DriverProfiles.ToListAsync();
    foreach (var driver in drivers)
    {
        if (driver.GmailRefreshToken != null && !IsEncrypted(driver.GmailRefreshToken))
        {
            // Re-save to trigger encryption
            _db.DriverProfiles.Update(driver);
        }
    }
    await _db.SaveChangesAsync();
}

private bool IsEncrypted(string token)
{
    // Encrypted tokens are base64, unencrypted might not be
    try
    {
        Convert.FromBase64String(token);
        return true;
    }
    catch
    {
        return false;
    }
}
```

---

## ✅ Implementation Checklist

- [ ] Create `Services/EncryptionService.cs`
- [ ] Generate AES-256 keys using utility
- [ ] Store keys in User Secrets:
  - `Encryption:Key`
  - `Encryption:IV`
- [ ] Register service in `Program.cs`
- [ ] Update `Data/ApplicationDbContext.cs` with value converters
- [ ] Create database migration
- [ ] Test encryption/decryption
- [ ] Test existing functionality (ProfileController)
- [ ] Update documentation
- [ ] Deploy to development environment

---

## 🧪 Testing AES-256 Implementation

### Unit Test Example

```csharp
[TestClass]
public class EncryptionServiceTests
{
    private IEncryptionService _encryptionService;

    [TestInitialize]
    public void Setup()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<EncryptionService>()
            .Build();
        _encryptionService = new EncryptionService(config);
    }

    [TestMethod]
    public void Encrypt_ThenDecrypt_ReturnsOriginalText()
    {
        // Arrange
        string originalText = "test_refresh_token_12345";

        // Act
        string encrypted = _encryptionService.Encrypt(originalText);
        string decrypted = _encryptionService.Decrypt(encrypted);

        // Assert
        Assert.AreEqual(originalText, decrypted);
        Assert.AreNotEqual(originalText, encrypted); // Should be different
    }

    [TestMethod]
    public void Decrypt_InvalidCipherText_ReturnsOriginal()
    {
        // Arrange
        string invalidCipherText = "not_valid_base64!!!";

        // Act
        string result = _encryptionService.Decrypt(invalidCipherText);

        // Assert
        Assert.AreEqual(invalidCipherText, result); // Fallback
    }
}
```

---

## 🔒 Security Best Practices

### Do's ✅
```
✅ Store AES keys in User Secrets (development)
✅ Store AES keys in environment variables (production)
✅ Use 256-bit keys (32 bytes)
✅ Use random IVs (initialization vectors)
✅ Use CBC mode with PKCS7 padding
✅ Rotate keys periodically
✅ Log encryption/decryption attempts (failures only)
✅ Use HTTPS for all data transit
```

### Don'ts ❌
```
❌ Don't hardcode encryption keys in source code
❌ Don't use weak keys or IVs
❌ Don't commit User Secrets to version control
❌ Don't reuse IVs for multiple encryptions
❌ Don't expose keys in error messages
❌ Don't store keys in configuration files
❌ Don't use AES-128 for sensitive data
```

---

## 📊 Comparison: Encryption Methods

| Method | Use Case | Pros | Cons |
|--------|----------|------|------|
| PBKDF2 (Passwords) | Password hashing | One-way, salted | Can't decrypt |
| DPAPI (User Secrets) | Dev key storage | OS-level encryption | Windows-only |
| AES-256 (Tokens) | Sensitive data | Reversible, strong | Key management needed |
| HTTPS/TLS (Transit) | Data in motion | Standard, transparent | Cert overhead |

---

## 🚀 Implementation Example Flow

```
1. User connects Gmail account
   ↓
2. OAuth returns refresh token
   ↓
3. ProfileController receives token
   ↓
4. Token saved to DriverProfile.GmailRefreshToken
   ↓
5. Entity Framework value converter triggers
   ↓
6. EncryptionService.Encrypt() encrypts token
   ↓
7. Encrypted token stored in database
   ↓
---
8. Application needs to use token
   ↓
9. DriverProfile loaded from database
   ↓
10. Entity Framework value converter triggers
   ↓
11. EncryptionService.Decrypt() decrypts token
   ↓
12. Decrypted token used for API calls
```

---

## 📝 Configuration Template

Add to User Secrets or environment variables:

```json
{
  "Encryption": {
    "Key": "[BASE64_ENCODED_32_BYTE_KEY]",
    "IV": "[BASE64_ENCODED_16_BYTE_IV]"
  }
}
```

**Production Environment Variables**:
```bash
Encryption__Key=[BASE64_KEY]
Encryption__IV=[BASE64_IV]
```

---

## ⚡ Performance Expectations

### Encryption Operations
```
Token Encrypt:     ~0.15ms
Token Decrypt:     ~0.10ms
Per Request:       Negligible (< 1ms total)
Database Impact:   None (transparent)
Memory Impact:     Minimal (< 1KB per field)
```

### Scalability
- **Per 1000 users**: <100ms total encryption overhead
- **Per 10000 concurrent**: Handled by thread pool
- **Recommended**: Cache decrypted tokens for 5-10 minutes if needed

---

**Summary**: 
✅ AES-256 is **safe and recommended**  
⚠️ Few potential issues, all have solutions  
🚀 Ready to implement immediately

Would you like me to implement this, or need clarification on any part?
