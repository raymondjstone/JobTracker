# Job Tracker

A Blazor application with browser extensions that automatically extract job listings from multiple job sites and save them to a local database for tracking and management.

## Supported Job Sites

| Site | Extension Folder | Status |
|------|-----------------|--------|
| LinkedIn | `BrowserExtension/` | Full support |
| Indeed | `IndeedExtension/` | Full support |
| S1Jobs | `S1JobsExtension/` | Full support |
| Welcome to the Jungle | `WTTJExtension/` | Full support |

## Features

### Job Extraction
- **Multi-site Support** - Extract jobs from LinkedIn, Indeed, S1Jobs, and Welcome to the Jungle
- **Automatic Extraction** - Jobs are extracted automatically as you browse
- **Description Capture** - Full job descriptions are captured when you view individual jobs
- **Auto-Fetch Descriptions** - Automatically navigate through jobs to capture missing descriptions
- **Duplicate Detection** - Prevents duplicate entries based on URL normalization
- **Source Tracking** - Each job is tagged with its source site

### Job Management
- **Dashboard** - View all jobs with statistics (total, interested, applied, etc.)
- **Tabbed Interface** - Browse, Possible, Applied, and Unsuitable tabs
- **Filtering** - Filter by search, location, job type, remote, salary, interest, source, and date
- **Title-Only Search** - Search specifically in job titles
- **Bulk Actions** - Mark all filtered jobs as Possible or Unsuitable

### Application Tracking
- **Interest Status** - Mark jobs as Interested, Not Interested, or Not Rated
- **Suitability Status** - Mark jobs as Possible, Unsuitable, or Not Checked
- **Applied Tracking** - Track which jobs you've applied to
- **Application Stages** - Track progress: Applied → No Reply → Pending → Tech Test → Interview → Offer (or Ghosted/Rejected)
- **Stage History** - View timeline of stage changes for each application

### Sharing & Export
- **WhatsApp Sharing** - Share job details directly to WhatsApp
- **Direct Links** - Open original job postings with one click

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Google Chrome or Microsoft Edge browser
- Accounts on job sites you want to use

## Installation

### 1. Set Up the Blazor Application

```bash
# Navigate to the project directory
cd JobTracker

# Restore dependencies
dotnet restore

# Run the application
dotnet run
```

The app will start at `https://localhost:7046`.

### 2. Install Browser Extensions

Each job site has its own extension. Install the ones you need:

1. Open Chrome/Edge and navigate to `chrome://extensions/` or `edge://extensions/`
2. Enable **Developer mode** (toggle in the top-right corner)
3. Click **Load unpacked**
4. Select the extension folder:
   - `BrowserExtension/` - LinkedIn
   - `IndeedExtension/` - Indeed
   - `S1JobsExtension/` - S1Jobs
   - `WTTJExtension/` - Welcome to the Jungle
5. Repeat for each extension you want to install

## Usage

### Extracting Jobs

1. **Start the Blazor app** - Make sure it's running (`dotnet run`)
2. **Navigate to a job site** - Go to LinkedIn Jobs, Indeed, S1Jobs, or WTTJ
3. **Browse jobs** - The extension automatically extracts job listings as you scroll
4. **View job details** - Click on individual jobs to capture full descriptions
5. **Check the indicator** - A colored pill in the bottom-right shows extraction status

### Auto-Fetch Descriptions

Each extension can automatically navigate through jobs to capture missing descriptions:

1. **Via Console** - Open DevTools (F12) and type:
   ```javascript
   // LinkedIn
   LJE.autoFetch(3)    // 3 second delay between jobs

   // Indeed
   IND.autoFetch(3)

   // S1Jobs
   S1J.autoFetch(3)

   // WTTJ
   WTTJ.autoFetch(3)
   ```

2. **Via Extension Popup** - Click the extension icon and press "Auto-Fetch Descriptions"

3. **Stop Auto-Fetch** - Click the Stop button in the UI or run:
   ```javascript
   LJE.stopAutoFetch()  // or IND, S1J, WTTJ
   ```

### Extension Console Commands

Each extension exposes commands in the browser console:

| Command | Description |
|---------|-------------|
| `XXX.extract()` | Manually trigger job extraction |
| `XXX.debug()` | Show debug info and page structure |
| `XXX.test()` | Test connection to the Blazor app |
| `XXX.status()` | Show extraction statistics |
| `XXX.getDesc()` | Get current job's description |
| `XXX.updateDesc()` | Send current description to server |
| `XXX.fetchMissing()` | Check for jobs needing descriptions |
| `XXX.autoFetch(n)` | Start auto-fetch with n second delay |
| `XXX.stopAutoFetch()` | Stop auto-fetching |
| `XXX.autoFetchStatus()` | Check auto-fetch progress |
| `XXX.help()` | Show all available commands |

Replace `XXX` with: `LJE` (LinkedIn), `IND` (Indeed), `S1J` (S1Jobs), or `WTTJ` (Welcome to the Jungle)

### Managing Jobs in the Web App

Navigate to `https://localhost:7046` to access the dashboard.

#### Tabs
- **Browse** - New jobs that haven't been categorized
- **Possible** - Jobs marked as potentially suitable
- **Applied** - Jobs you've applied to
- **Unsuitable** - Jobs that don't match your criteria

#### Job Cards
- Click the card header to open the job on the original site
- Use thumbs up/down buttons to set interest
- Use Possible/Unsuitable buttons to categorize
- Click "Applied" to track applications
- Use the stage dropdown to update application progress
- Click WhatsApp icon to share
- Click "View Full Description" for details

#### Filtering Options

| Filter | Description |
|--------|-------------|
| Search | Search in title, description, and company |
| Title Only | Search only in job titles |
| Location | Filter by job location |
| Job Type | Full-time, Part-time, Contract, etc. |
| Remote | Remote only or On-site only |
| Source | LinkedIn, Indeed, S1Jobs, or WTTJ |
| Interest | Interested, Not Interested, or Not Rated |
| Salary Info | Has salary or No salary listed |
| Salary Contains | Search within salary text (e.g., "100K") |
| Stage | Filter applied jobs by application stage |
| Date Range | Filter by date posted |
| Sort By | Sort by date added, date posted, title, or company |

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/jobs` | Get all jobs |
| POST | `/api/jobs` | Add a new job (with duplicate detection) |
| GET | `/api/jobs/count` | Get total job count |
| GET | `/api/jobs/stats` | Get job statistics |
| GET | `/api/jobs/exists?url=...` | Check if job URL exists |
| PUT | `/api/jobs/{id}/interest` | Update interest status |
| PUT | `/api/jobs/description` | Update job description by URL |
| GET | `/api/jobs/needing-descriptions` | Get jobs missing descriptions |
| POST | `/api/jobs/cleanup` | Clean up job titles |
| POST | `/api/jobs/remove-duplicates` | Remove duplicate jobs |
| POST | `/api/jobs/fix-sources` | Fix unknown sources from URLs |
| DELETE | `/api/jobs/{id}` | Delete a job |
| DELETE | `/api/jobs/clear` | Delete all jobs |

## Data Storage

The app supports two storage backends, controlled by the `StorageProvider` setting in `appsettings.json`.

### JSON (default)

Jobs, history, and settings are stored as JSON files in the `Data/` directory:

- `Data/jobs.json` - Job listings
- `Data/history.json` - Audit/history log
- `Data/settings.json` - App settings and rules

No configuration needed - this is the default when `StorageProvider` is `"Json"` or not set.

### SQL Server

To switch to SQL Server, update `appsettings.json`:

```json
{
  "StorageProvider": "SqlServer",
  "ConnectionStrings": {
    "JobSearchDb": "Server=myserver;Database=JobSearch;User Id=myuser;Password=mypassword;TrustServerCertificate=True"
  }
}
```

On first startup with SQL Server:
1. The database and tables are created automatically
2. If the JSON data files exist and the database is empty, all data is imported automatically
3. No manual migration steps are needed

To switch back to JSON, set `"StorageProvider": "Json"`. The JSON files are never modified while running in SQL Server mode, so they remain as they were.

### Tables (SQL Server)

| Table | Contents |
|-------|----------|
| `JobListings` | All tracked jobs |
| `HistoryEntries` | Audit log of changes |
| `JobRules` | Auto-classification rules |
| `AppSettings` | Single-row config (site URLs, rule settings) |

## Project Structure

```
JobTracker/
├── Components/
│   ├── Pages/
│   │   ├── Jobs.razor       # Main job dashboard
│   │   └── AddJob.razor     # Manual job entry
│   └── Layout/
│       └── NavMenu.razor    # Navigation menu
├── Models/
│   └── JobListing.cs        # Job data model
├── Services/
│   └── JobListingService.cs # Job storage, filtering, source detection
├── Data/
│   └── jobs.json            # Persistent job storage
├── BrowserExtension/        # LinkedIn extension
│   ├── manifest.json
│   ├── content.js
│   ├── popup.html
│   └── popup.js
├── IndeedExtension/         # Indeed extension
│   ├── manifest.json
│   ├── content.js
│   ├── popup.html
│   └── popup.js
├── S1JobsExtension/         # S1Jobs extension
│   ├── manifest.json
│   ├── content.js
│   ├── popup.html
│   └── popup.js
├── WTTJExtension/           # Welcome to the Jungle extension
│   ├── manifest.json
│   ├── content.js
│   ├── popup.html
│   └── popup.js
└── Program.cs               # App configuration and API endpoints
```

## Troubleshooting

### Extension not detecting jobs

1. Make sure you're on a job listing page
2. Open DevTools (F12) → Console tab
3. Look for `[LJE]`, `[IND]`, `[S1J]`, or `[WTTJ]` messages
4. Run the `debug()` command to see page structure
5. Try the `extract()` command to manually trigger

### Connection failed

1. Verify the Blazor app is running (`dotnet run`)
2. Visit `https://localhost:7046` directly to accept any certificate warnings
3. Check the extension popup shows "Connected"

### Jobs not appearing in the app

1. Check the Blazor app console for `[API]` messages
2. Verify the job was added (check for "Added:" in extension console)
3. Jobs might be filtered - try clearing all filters

### Missing descriptions

1. Click on individual jobs to capture their descriptions
2. Use auto-fetch to automatically capture descriptions:
   ```javascript
   LJE.autoFetch(3)  // Replace LJE with appropriate prefix
   ```

### Unknown source on jobs

Run the fix-sources endpoint to infer sources from URLs:
```bash
curl -X POST https://localhost:7046/api/jobs/fix-sources
```

### Duplicate text in job titles

Run the cleanup endpoint:
```bash
curl -X POST https://localhost:7046/api/jobs/cleanup
```

## Application Stages

When tracking applications, jobs progress through these stages:

| Stage | Description |
|-------|-------------|
| Applied | Initial application submitted |
| No Reply | No response received yet |
| Pending | Awaiting decision |
| Ghosted | No response after extended time |
| Rejected | Application was rejected |
| Tech Test | Technical assessment stage |
| Interview | Interview stage |
| Offer | Received job offer |

Click the stage badge on a job card to cycle through stages, or use the dropdown to select a specific stage.

## License

This project is for personal use to track job applications.

## Contributing

Feel free to submit issues and enhancement requests!
