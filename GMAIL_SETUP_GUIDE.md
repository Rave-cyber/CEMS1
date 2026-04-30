# Gmail Connection Setup Guide for CEMS

## Overview
This implementation allows Super Admin users to connect Gmail accounts for each user profile in the CEMS system. The Gmail connection stores the email address and refresh token securely in the database for future use.

## What's Been Added

### 1. Database Schema Changes
- Added two new fields to all profile models:
  - `GmailAddress` - Stores the connected Gmail email address
  - `GmailRefreshToken` - Stores the OAuth2 refresh token for Gmail API access
- Migration: `AddGmailConnectionToProfiles`

### 2. New Services
- **IGmailService** - Interface for Gmail OAuth2 operations
- **GmailService** - Implementation handling:
  - OAuth2 authorization URL generation
  - Authorization code exchange for tokens
  - Access token refresh functionality
  - Error handling and retries

### 3. ProfileController Enhancements
New endpoints for Gmail management:
- `GET /profile/gmail-connect` - Initiates Gmail OAuth flow
- `GET /profile/gmail-callback` - Handles OAuth callback and stores credentials
- `POST /profile/disconnect-gmail` - Removes Gmail connection

### 4. Configuration
Added `Gmail` section to all `appsettings.json` files with:
```json
"Gmail": {
  "ClientId": "YOUR_GOOGLE_CLIENT_ID",
  "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET",
  "RedirectUri": "https://localhost:7000/profile/gmail-callback"
}
```

## Setup Instructions

### Step 1: Google Cloud Console Setup
1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Navigate to **APIs & Services** > **Credentials**
4. Click **Create Credentials** > **OAuth 2.0 Client IDs**
5. Select **Web application**
6. Add authorized redirect URIs:
   - Development: `https://localhost:7000/profile/gmail-callback`
   - Production: `https://yourdomain.com/profile/gmail-callback`
7. Copy the Client ID and Client Secret

### Step 2: Configure Application Settings
1. Update `appsettings.Development.json`:
   ```json
   "Gmail": {
     "ClientId": "YOUR_GOOGLE_CLIENT_ID",
     "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET",
     "RedirectUri": "https://localhost:7000/profile/gmail-callback"
   }
   ```

2. Update `appsettings.Production.json` with production URLs

### Step 3: Database Migration
The migration has been created. Update your database:
```bash
dotnet ef database update
```

### Step 4: Test the Implementation
1. Run the application
2. Log in as a user
3. Visit `/profile/gmail-connect` to initiate Gmail connection
4. Authorize the application when prompted by Google
5. You'll be redirected back and the Gmail address will be stored

## Updated Files

### Models
- `Models/DriverProfile.cs` - Added Gmail fields
- `Models/CEOProfile.cs` - Added Gmail fields
- `Models/ManagerProfile.cs` - Added Gmail fields
- `Models/FinanceProfile.cs` - Added Gmail fields

### Services
- `Services/IGmailService.cs` - New interface (created)
- `Services/GmailService.cs` - New implementation (created)

### Controllers
- `Controllers/ProfileController.cs` - Added Gmail methods and service injection

### Configuration
- `Program.cs` - Registered IGmailService and added session support
- `appsettings.json` - Added Gmail configuration section
- `appsettings.Development.json` - Added Gmail development settings
- `appsettings.Production.json` - Added Gmail production settings

### Database
- Migration: `AddGmailConnectionToProfiles` (auto-generated)

## API Usage

### Connect Gmail
```
GET /profile/gmail-connect
```
Redirects user to Google OAuth consent screen.

### Gmail Callback
```
GET /profile/gmail-callback?code=AUTH_CODE&state=STATE_VALUE
```
Handled automatically after user authorization.

### Get Profile (Updated)
```
GET /profile/get-profile
```
Returns profile data including:
- `gmailAddress` - Connected email
- `isGmailConnected` - Boolean flag

### Disconnect Gmail
```
POST /profile/disconnect-gmail
```
Removes Gmail connection from user profile.

## Security Considerations
1. **Refresh Tokens**: Stored securely in database, should be encrypted in production
2. **State Parameter**: Used to prevent CSRF attacks during OAuth flow
3. **Session Management**: Configured with 30-minute timeout
4. **HTTPS**: Ensure all production URLs use HTTPS
5. **Credentials**: Never commit real credentials - use environment variables or user secrets

## Environment Variables (Recommended for Production)
```bash
Gmail__ClientId=YOUR_CLIENT_ID
Gmail__ClientSecret=YOUR_CLIENT_SECRET
Gmail__RedirectUri=https://yourdomain.com/profile/gmail-callback
```

## Future Enhancements
1. Send emails through Gmail API using stored credentials
2. Sync Gmail labels and folders
3. Automatic token refresh on expiration
4. Multiple Gmail account support per user
5. Encryption of refresh tokens at rest

## Troubleshooting

### "Client ID not configured"
- Ensure Gmail settings are in appsettings.json
- Check that ClientId and ClientSecret are not empty

### "Redirect URI mismatch"
- Verify redirect URI in Google Cloud Console matches your application
- Include protocol (https://) and exact path

### "State mismatch error"
- Clear browser cookies or use private/incognito window
- Ensure sessions are working properly

## Notes
- The implementation uses `user.Email` as the Gmail address after authorization
- Refresh tokens are stored to enable long-lived Gmail API access
- Current implementation focuses on email connection; sending via Gmail API requires additional setup
