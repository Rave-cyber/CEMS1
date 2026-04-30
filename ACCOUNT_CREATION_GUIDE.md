# CEMS Account Creation & Gmail Connection Guide

## 🎯 Overview
This guide explains how to:
1. **Create new user accounts** (SuperAdmin only)
2. **Connect Gmail accounts** (All users)
3. **Manage profile information**

---

## Part 1: Creating a New Account (SuperAdmin)

### Step 1: Navigate to User Management
1. Log in as **SuperAdmin**
2. Go to **SuperAdmin Dashboard** → **Users** (or directly to `/SuperAdmin/Users`)

### Step 2: Click "Create Account" Button
- Locate the **"Create Account"** button in the filter section
- Click it to open the account creation modal

### Step 3: Fill in Step 1 - Credentials
**Required Fields:**
- **Email Address** - Use a valid email (this is the login username)
- **Password** - Minimum 6 characters
- **Confirm Password** - Re-enter the same password

Example:
```
Email: john.driver@company.com
Password: SecurePass123
```

Click **Next** to continue

### Step 4: Fill in Step 2 - Personal Information
**Required Fields:**
- **Full Name** - Employee's complete name
- **Contact Number** - Phone number (20 characters max)
- **License Number** (Drivers only) - Driver's license number

Example:
```
Full Name: Juan Dela Cruz
Contact Number: 09171234567
License Number: D04-2023-123456
```

Click **Next** to continue

### Step 5: Fill in Step 3 - Address
**Required Fields:**
- **Province** - Select from dropdown (type to search)
- **City** - Will auto-populate based on province
- **Barangay** - Will auto-populate based on city
- **Street Address** - Full street address
- **Zip Code** - Postal code
- **Country** - Defaults to "Philippines"

Example:
```
Province: Metro Manila
City: Quezon City
Barangay: Barangay Krus na Ligas
Street: 123 Quezon Avenue
Zip Code: 1110
Country: Philippines
```

Click **Next** to continue

### Step 6: Review and Confirm
- Review all entered information
- Click **Create Account** to finalize

✅ **Account successfully created!**

---

## Part 2: Connecting Gmail Account

### Who Can Connect?
- ✅ CEO
- ✅ Manager
- ✅ Finance
- ✅ Driver
- ✅ SuperAdmin

### Step 1: Open Profile Modal
1. Click your **profile icon** in the top-right corner
2. Click **"My Profile"** from the dropdown menu
3. The profile modal will open

### Step 2: Locate Gmail Section
Scroll to find the **Gmail Account** section with:
- Gmail status (Connected/Not connected)
- Connect button (red with Gmail logo)
- Disconnect button (appears if already connected)

### Step 3: Click "Connect"
1. Click the **"Connect"** button
2. You'll be redirected to **Google's login page**

### Step 4: Authorize CEMS
1. Sign in with your **Gmail account**
2. On the consent screen, review permissions
3. Click **"Allow"** to authorize CEMS

### Step 5: Completion
✅ You'll be redirected back automatically
✅ Gmail status will change to **"Connected"**
✅ Your Gmail address will display in the profile

### Troubleshooting Connection

**Issue: "Redirect URI mismatch"**
- Solution: Ensure your URL matches exactly in Google Console settings
- Development: `https://localhost:7000/profile/gmail-callback`
- Production: `https://yourdomain.com/profile/gmail-callback`

**Issue: Button doesn't work**
- Solution: Clear browser cache or use incognito window
- Ensure cookies are enabled

**Issue: Page stays blank after authorization**
- Solution: Wait 5-10 seconds for redirect
- Check browser console for errors (F12)

---

## Part 3: Managing Your Profile

### Update Profile Information
1. Open **Profile Modal** (My Profile)
2. Edit any of these fields:
   - Full Name
   - Contact Number
   - Address (Street, Barangay, City, Province, Zip, Country)
3. Click **"Save Changes"**

### Upload Profile Photo
1. In the profile modal, click on your **profile picture** circle
2. Select an image file:
   - ✅ Supported: JPG, PNG, GIF, WebP
   - ❌ Max size: 2MB
3. Photo updates instantly

### Disconnect Gmail
1. If Gmail is connected, click **"Disconnect"** button
2. Confirm the action
3. Gmail will be removed from your profile
4. You can reconnect anytime

---

## Account Roles & Permissions

### CEO
- Full system access
- Approve/reject expense reports
- Manage budgets
- View analytics
- Can connect Gmail

### Manager
- Manage driver expenses
- Review reports before CEO
- Track team budgets
- Can connect Gmail

### Finance
- Process payments
- View reimbursement requests
- Handle financial reconciliation
- Can connect Gmail

### Driver
- Submit expense reports
- View personal expenses
- Track reimbursement status
- Can connect Gmail

### SuperAdmin
- Create/edit user accounts
- Manage roles and permissions
- View audit logs
- Manage fuel prices
- System configuration
- Can connect Gmail

---

## Step-by-Step: Account Creation Example

**Create a Driver Account:**

```
STEP 1 - CREDENTIALS
├─ Email: alex.ramirez@company.com
├─ Password: Driver@2024
└─ Confirm: Driver@2024

STEP 2 - PERSONAL INFO
├─ Full Name: Alexander Ramirez
├─ Contact Number: 09189876543
└─ License Number: D05-2023-789456

STEP 3 - ADDRESS
├─ Province: Metro Manila
├─ City: Pasig City
├─ Barangay: Barangay Caniogan
├─ Street: 456 Sycamore St.
├─ Zip Code: 1600
└─ Country: Philippines

STEP 4 - REVIEW & CREATE
└─ ✅ Account Created!
```

---

## Gmail Connection Example

**User Workflow:**

```
1. Driver logs in with email/password
2. Opens My Profile (dropdown → My Profile)
3. Scrolls to Gmail Account section
4. Clicks "Connect" button
5. Redirected to Google login
6. Enters Gmail credentials
7. Clicks "Allow" on consent screen
8. Automatically redirected back
9. Sees "✓ Gmail Connected"
10. Gmail email appears in profile
```

---

## Important Notes

### ⚠️ Security Tips
- **Never share your password** - SuperAdmin will never ask
- **Unique passwords** - Use different password for CEMS
- **Secure Gmail** - Protect your Google account (2FA enabled)
- **Regular updates** - Update profile info when it changes

### ℹ️ Account Management
- One Gmail per user
- Can disconnect and reconnect anytime
- Gmail data is encrypted in database
- Profile photos are stored in secure location

### 🔒 Privacy
- Gmail access is scoped (read-only)
- Only your email address is stored
- No automatic emails are sent
- Manual approval required for any actions

---

## Contact Support

If you encounter issues:
1. Check browser console (F12)
2. Try clearing cache and cookies
3. Use incognito window to test
4. Contact your **SuperAdmin** for account issues
5. Contact **IT Support** for Gmail authorization errors

---

## FAQ

**Q: Can I create my own account?**
A: No, only SuperAdmin can create accounts for security reasons.

**Q: Can I use multiple Gmail accounts?**
A: One account per user in CEMS, but you can disconnect/reconnect different Gmail accounts.

**Q: What happens if I forget my password?**
A: Click "Forgot Password" on the login page or contact SuperAdmin for a password reset.

**Q: Is my Gmail account shared with others?**
A: No, your Gmail connection is private and only accessible to you.

**Q: Can I see other users' Gmail addresses?**
A: No, Gmail information is only visible in your own profile.

---

**Version:** 1.0  
**Last Updated:** April 2026  
**Status:** Ready for Production
