# LinkedIn Job Extractor - Chrome Extension

A Chrome extension that automatically extracts job listings from LinkedIn and sends them to your local Blazor job tracking application.

## Installation

### Step 1: Load the Extension

1. Open Google Chrome
2. Navigate to `chrome://extensions/`
3. Enable **Developer mode** using the toggle in the top-right corner
4. Click **Load unpacked**
5. Select this `BrowserExtension` folder
6. The extension icon (Li) should appear in your toolbar

### Step 2: Pin the Extension (Optional)

1. Click the puzzle piece icon in Chrome's toolbar
2. Find "LinkedIn Job Extractor"
3. Click the pin icon to keep it visible

## Configuration

1. Click the extension icon to open the popup
2. **Blazor App URL**: Enter your Blazor app's URL (default: `https://localhost:7046`)
3. Click **Test Connection** to verify the app is running and accessible

## Usage

### Automatic Extraction

1. Make sure your Blazor app is running
2. Go to [linkedin.com/jobs](https://www.linkedin.com/jobs/)
3. Browse job listings - they are extracted automatically
4. Watch the blue indicator in the bottom-right corner for status

### Manual Extraction

- **On LinkedIn**: Click the blue indicator pill
- **From popup**: Click **Extract Jobs Now**

### Status Indicator

The blue pill indicator shows:
- **Ready** - Extension loaded, waiting
- **Scanning...** - Currently extracting jobs
- **Added!** - Job successfully sent to app
- **Duplicate** - Job already exists in app
- **Error** - Connection or server error

The number shows how many jobs were sent in this session.

## Supported LinkedIn Pages

The extension works on:
- `linkedin.com/jobs/` - Jobs homepage
- `linkedin.com/jobs/search/` - Search results
- `linkedin.com/jobs/view/` - Individual job pages
- `linkedin.com/jobs/collections/` - Saved jobs

## Debugging

Open the browser console (F12 ? Console) on LinkedIn and use:

```javascript
LJE.extract()   // Manually trigger extraction
LJE.test()      // Test connection to server
LJE.getDesc()   // Preview the job description that would be extracted
LJE.status()    // Show session statistics
LJE.sendTest()  // Send a test job to verify connectivity
LJE.debug()     // Show details about found job links
```

## Troubleshooting

### No messages in console

- Make sure you're on a LinkedIn `/jobs/` page
- Reload the page after installing/updating the extension
- Check that the extension is enabled in `chrome://extensions/`

### Connection errors

- Verify the Blazor app is running (`dotnet run`)
- Check the URL in the extension popup
- For HTTPS, visit the URL directly first to accept any certificate warnings

### Jobs not being extracted

- LinkedIn's page structure may have changed
- Check for `[LJE]` messages in the console for errors
- Try scrolling or clicking on different jobs

## Files

| File | Purpose |
|------|---------|
| `manifest.json` | Extension configuration |
| `content.js` | Runs on LinkedIn pages, extracts job data |
| `popup.html` | Extension popup interface |
| `popup.js` | Popup functionality and settings |
| `icons/` | Extension icons (16, 32, 48, 128px) |

## Permissions

The extension requests:
- **activeTab** - To interact with LinkedIn pages
- **storage** - To save your settings
- **Host permissions** - Access to LinkedIn and localhost for the API

## Privacy

- All data stays on your local machine
- No data is sent to external servers
- Jobs are only sent to your configured Blazor app URL
