# Quick Reference: Gmail & Account Features

## 🚀 Quick Start

### For SuperAdmin: Create an Account
1. **Dashboard** → **Users**
2. Click **"Create Account"** button
3. Fill 4 steps:
   - Step 1: Email + Password
   - Step 2: Name + Contact
   - Step 3: Address (Province → City → Barangay)
   - Step 4: Review & Create
4. ✅ Account created!

### For Any User: Connect Gmail
1. Click **profile icon** (top-right)
2. Click **"My Profile"**
3. Scroll to **Gmail Account** section
4. Click **"Connect"** button
5. Sign in with Gmail
6. Click **"Allow"** on consent screen
7. ✅ Gmail connected!

---

## 📋 Required Google Setup

Before enabling Gmail:

```
1. Go to Google Cloud Console
2. Create OAuth 2.0 credentials
3. Add Redirect URIs:
   - https://localhost:7000/profile/gmail-callback (dev)
   - https://yourdomain.com/profile/gmail-callback (prod)
4. Copy Client ID & Secret
5. Update appsettings.json
```

**appsettings.json:**
```json
"Gmail": {
    "ClientId": "YOUR_ID.apps.googleusercontent.com",
    "ClientSecret": "YOUR_SECRET",
    "RedirectUri": "https://localhost:7000/profile/gmail-callback"
}
```

---

## 🔗 Key URLs

| Feature | URL |
|---------|-----|
| Create Account | `/SuperAdmin/Users` |
| My Profile | Click profile icon → "My Profile" |
| Gmail Connect | `/Profile/GmailConnect` (auto-triggered) |
| Gmail Callback | `/Profile/GmailCallback` (auto-handled) |
| Disconnect Gmail | `POST /Profile/DisconnectGmail` |
| Get Profile | `GET /Profile/GetProfile` |

---

## 📊 What Gets Stored

### In Database (User Profile):
- ✅ Gmail Email Address
- ✅ Gmail Refresh Token
- ✅ Full Name
- ✅ Contact Number
- ✅ Street Address
- ✅ Barangay
- ✅ City
- ✅ Province
- ✅ Zip Code
- ✅ Country
- ✅ Profile Photo Path

### NOT Stored:
- ❌ Gmail password
- ❌ Access token (regenerated)
- ❌ User password (hashed)

---

## ⚙️ Configuration Files

### Development
`appsettings.Development.json`

### Production
`appsettings.Production.json`

### Base Settings
`appsettings.json`

---

## 🗄️ Database

### Migration Applied:
```
20260428235149_AddGmailConnectionToProfiles
```

### Tables Updated:
- CEOProfiles
- ManagerProfiles
- FinanceProfiles
- DriverProfiles

### Fields Added:
- GmailAddress (nvarchar(255))
- GmailRefreshToken (nvarchar(500))

---

## 👥 User Roles

### Can Create Accounts:
- 🔑 SuperAdmin

### Can Connect Gmail:
- ✅ SuperAdmin
- ✅ CEO
- ✅ Manager
- ✅ Finance
- ✅ Driver

### Can Create Own Profile:
- ❌ No one (SuperAdmin creates)

---

## 🎯 Common Tasks

### Task: Create a Driver Account
```
1. SuperAdmin → Users
2. Create Account
3. Step 1: driver@company.com / SecurePass123
4. Step 2: John Dela Cruz / 09171234567 / DL-2024-12345
5. Step 3: Metro Manila → Quezon City → Barangay N → Street → 1111
6. Review & Create ✅
```

### Task: Connect Gmail as CEO
```
1. Click profile icon
2. My Profile
3. Find Gmail Section
4. Click Connect
5. Sign in with Gmail account
6. Click Allow
7. ✅ Connected (email shows in profile)
```

### Task: Disconnect Gmail
```
1. Profile → My Profile
2. Find Gmail Section
3. Click Disconnect
4. ✅ Disconnected (Connect button reappears)
```

### Task: Update Profile Info
```
1. Profile → My Profile
2. Edit any fields:
   - Name
   - Contact
   - Address
   - etc.
3. Click Save Changes ✅
```

### Task: Upload Profile Photo
```
1. Profile → My Profile
2. Click profile picture
3. Select JPG/PNG/GIF/WebP (max 2MB)
4. ✅ Photo updates automatically
```

---

## ❌ Troubleshooting

### Gmail Connect Button Not Working
**Solution:** 
- Clear browser cache
- Try incognito mode
- Check Google Console redirect URI

### Redirect URI Mismatch Error
**Solution:**
- Add exact URL to Google Console
- Include protocol (https://)
- Match appsettings.json exactly

### Gmail Status Shows "Not Connected"
**Solution:**
- Refresh browser (F5)
- Log out and back in
- Try connecting again

### "Invalid State Parameter"
**Solution:**
- Clear cookies
- Session may have expired
- Try again in new browser window

---

## 🔐 Security Notes

✅ **Safe:**
- Gmail credentials never stored
- Refresh tokens encrypted in database
- OAuth tokens regenerated
- State parameter prevents CSRF
- Session times out after 30 min

⚠️ **Remember:**
- Only SuperAdmin can create accounts
- Each user has unique Gmail connection
- Disconnect removes Gmail access
- Passwords are hashed in database

---

## 📞 Support

### Account Creation Issues
→ Contact **SuperAdmin**

### Gmail Connection Issues
→ Try setup guide: `GMAIL_SETUP_GUIDE.md`

### Profile Issues
→ Check `ACCOUNT_CREATION_GUIDE.md`

### Technical Issues
→ Check browser console (F12)

---

## 📚 Related Documents

1. **GMAIL_SETUP_GUIDE.md** - Detailed setup instructions
2. **ACCOUNT_CREATION_GUIDE.md** - End-user guide
3. **IMPLEMENTATION_SUMMARY.md** - Technical overview

---

## ✅ Verification Checklist

After setup, verify:

- [ ] SuperAdmin can access Users page
- [ ] Create Account button visible
- [ ] Can create test account
- [ ] New user can login
- [ ] My Profile button works
- [ ] Gmail Connect button visible
- [ ] Can authorize Gmail
- [ ] Gmail shows as connected
- [ ] Email address displays
- [ ] Can disconnect Gmail
- [ ] Can upload profile photo
- [ ] Address fields work
- [ ] Changes persist on reload

---

**Version:** 1.0  
**Last Updated:** April 2026  
**Status:** Ready to Use
