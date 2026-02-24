# Browser Extension API Authentication Setup

## Problem
The browser extensions are getting 400/401 errors when trying to send jobs to the Blazor app because authentication was recently added to the API endpoints.

## Solution
The extensions have been updated to support API key authentication via the `X-API-Key` header.

## What Was Changed

### 1. BrowserExtension (LinkedIn)
- **content.js**: Added API key loading from Chrome storage and `getHeaders()` function
- **popup.js**: Added API key field handling and storage
- **popup.html**: Added "API Key (User ID)" input field

### 2. S1JobsExtension
- **content.js**: Added API key loading and `getHeaders()` function
- **popup.js**: Added API key field handling
- **popup.html**: Added "API Key (User ID)" input field

### 3. Program.cs (Server)
- Added detailed logging to diagnose API requests
- Enhanced CORS policy with `WithExposedHeaders`

## How to Use the Extensions

### Step 1: Get Your User ID
1. Log into the Blazor Job Tracker app
2. Navigate to your profile or settings page
3. Copy your User ID (it's a GUID like `12345678-1234-1234-1234-123456789abc`)

### Step 2: Configure the Extension
1. Click the extension icon in your browser
2. Enter your User ID in the "API Key (User ID)" field
3. The extension will save this and use it for all API requests

### Step 3: Test the Connection
1. Click "Test Connection" in the extension popup
2. You should see "Connected" with a green dot
3. The stats should load correctly

## API Authentication Flow

The server checks for authentication in this order:
1. **API Key Header** (`X-API-Key`): Format is just the user ID (GUID)
2. **Cookie Authentication**: Falls back to browser cookies if no API key

```javascript
// Extension sends this header with every request:
headers: {
  'Content-Type': 'application/json',
  'Accept': 'application/json',
  'X-API-Key': 'your-user-id-guid'
}
```

## Troubleshooting

### "hasApiKey: false" in console
- The API key is not configured in the extension
- Solution: Enter your User ID in the extension popup settings

### "400: The request has an incorrect Content-type"
- This error occurs when the Content-Type header is missing or wrong
- The extensions now send `Content-Type: application/json` properly
- If you still see this, try refreshing the LinkedIn/S1Jobs page after configuring the API key

### "401 Unauthorized"
- The API key (User ID) is invalid or the user doesn't exist
- Solution: Double-check the User ID matches your account in the Blazor app

### Jobs not appearing
1. Make sure the API key is configured
2. Check the extension console (F12) for errors
3. Look at the server logs for more details
4. Verify you're logged into the Blazor app OR have the correct API key set

## For Development

If you want to test without authentication (NOT recommended for production):
- Comment out the authentication check in the API endpoints
- Or always return a valid user ID instead of checking authentication

Example for testing only:
```csharp
// TESTING ONLY - remove this
var userId = Guid.Parse("your-test-user-id");
// Instead of:
// var userId = GetUserIdFromRequest(context, authService);
```

## API Endpoints Reference

All endpoints now require authentication via API key or cookie:

- `POST /api/jobs` - Add a job
- `GET /api/jobs` - Get all jobs for the authenticated user
- `GET /api/jobs/count` - Get job count
- `GET /api/jobs/stats` - Get statistics
- `GET /api/jobs/needing-descriptions` - Get jobs without descriptions
- `PUT /api/jobs/description` - Update job description
- `POST /api/jobs/remove-duplicates` - Remove duplicate jobs
- `DELETE /api/jobs/clear` - Delete all jobs (with confirmation)
- `POST /api/jobs/cleanup` - Cleanup job titles
- `POST /api/jobs/clean-descriptions` - Clean description boilerplate
