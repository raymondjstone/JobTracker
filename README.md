# Job Tracker 🚀

A **comprehensive AI-powered** Blazor application with browser extensions that automatically extract job listings from multiple job sites, analyze them with AI, and help you land your dream job faster.

![Job Tracker Dashboard](images/dashboard.png)

## 🌟 What's New - AI Job Assistant

**JobTracker now includes a complete AI-powered job search assistant!** Leverage OpenAI GPT or Anthropic Claude to:

- 📊 **Auto-analyze job descriptions** - Extract key requirements, responsibilities, and skills
- 📝 **Generate cover letters** - Personalized suggestions based on your profile
- 🎯 **Identify skill gaps** - See what you have vs what's needed (with match %)
- 💼 **Prep for interviews** - Get likely technical and behavioral questions
- 💰 **Negotiate salaries** - Market rates, strategies, and scripts
- 📄 **Optimize resumes** - ATS keywords and tailored summaries
- 🔮 **Predict success** - AI estimates your application success probability
- 🔍 **Find similar jobs** - Discover related opportunities automatically

**Choose your AI provider**: OpenAI (GPT-3.5, GPT-4) or Anthropic Claude (3.5 Sonnet, Opus, etc.)

## Supported Job Sites

| Site | Extension Folder | Console Prefix | Status |
|------|-----------------|----------------|--------|
| LinkedIn | `BrowserExtensions/LinkedInExtension/` | `LJE` | Full support |
| Indeed | `BrowserExtensions/IndeedExtension/` | `IND` | Full support |
| S1Jobs | `BrowserExtensions/S1JobsExtension/` | `S1J` | Full support |
| Welcome to the Jungle | `BrowserExtensions/WTTJExtension/` | `WTTJ` | Full support |
| EnergyJobSearch | `BrowserExtensions/EnergyJobSearchExtension/` | `EJS` | Full support |

## Features

### 🤖 AI Job Assistant (NEW!)
- **Dual Provider Support** - Choose between OpenAI or Anthropic Claude
- **Job Analysis** - AI extracts summary, responsibilities, required skills, qualifications, and nice-to-have skills
- **Cover Letter Generation** - Personalized opening, selling points, and closing based on your profile
- **Skill Gap Analysis** - Visual comparison of your skills vs job requirements with match percentage
- **Similar Jobs** - AI-powered recommendations based on skills, company, job type, and more
- **Interview Preparation** - Generate 5 technical questions, 5 behavioral questions, and questions to ask them
- **Salary Negotiation** - Market rate estimates, negotiation strategies, value points, and scripts
- **Resume Optimization** - ATS keywords, skills to highlight, and tailored professional summary
- **Success Prediction** - AI predicts application success probability with strength/risk factors
- **One-Click Analysis** - "Analyze with AI" button on every job card
- **Persistent Results** - All AI insights saved to job records for instant access
- **Model Selection** - Choose from GPT-3.5 Turbo, GPT-4, GPT-4o, Claude 3.5 Sonnet, Claude 3 Opus, etc.
- **Cost Optimization** - Switch models based on your budget and quality needs

### Job Extraction
- **Multi-site Support** - Extract jobs from LinkedIn, Indeed, S1Jobs, Welcome to the Jungle, and EnergyJobSearch
- **Automatic Extraction** - Jobs are extracted automatically as you browse
- **Description Capture** - Full job descriptions are captured when you view individual jobs
- **In-Page Description Fetch** - Automatically clicks through job cards on list/collection pages to capture all descriptions
- **Auto-Fetch Descriptions** - Automatically navigate through jobs to capture missing descriptions
- **Duplicate Detection** - Prevents duplicate entries based on URL normalization
- **Source Tracking** - Each job is tagged with its source site

### Job Management
- **Dashboard** - View all jobs with statistics (total, interested, applied, etc.)
- **Tabbed Interface** - Browse, Possible, Applied, Pipeline (Kanban), Archived, and Unsuitable tabs
- **Pinned Jobs** - Pin important jobs to the top of any tab
- **Filtering** - Filter by search, location, job type, remote, salary, interest, source, skills, and date
- **Notes & Cover Letter Search** - Search finds matches in job titles, descriptions, companies, notes, and cover letters
- **Title-Only Search** - Search specifically in job titles
- **Salary Range Filter** - Parse salary strings into min/max values and filter by target salary
- **Skill Filter** - Filter by skill with autocomplete from existing job skills
- **Saved Filter Presets** - Save and load named filter configurations
- **Bulk Actions** - Mark all filtered jobs as Possible or Unsuitable
- **Change Tracking** - Track job listing changes over time with visual indicators:
  - **"Updated" Badge** - Orange badge appears when job details change (description, salary, location, etc.)
  - **Change Timeline** - View detailed change history with "View Changes" button
  - **Auto-Tracking** - Changes are automatically detected when descriptions are fetched or updated
  - **Change Impact Levels** - Major (title, company, salary), Moderate (location, description), and Minor changes
  - **First Description Tracking** - Tracks when a job description is first added after initial import

### Application Tracking
- **Interest Status** - Mark jobs as Interested, Not Interested, or Not Rated
- **Suitability Status** - Mark jobs as Possible, Unsuitable, or Not Checked
- **ML-Based Job Scoring** - Automatic 0-100 scoring based on your preferences and past behavior (similar to JobSync)
  - Skills match weighting
  - Salary range matching
  - Remote preference
  - Location preferences
  - Keyword matching (must-have and avoid)
  - Company preferences
  - Learning from your past application patterns
  - Configurable weights for each factor
  - Visual score badges on job cards (color-coded by score)
  - Progress indicator during recalculation
  - Skips unsuitable jobs during recalculation
- **Score-Based Rules** - Create rules using ML scores (e.g., "Auto-mark jobs with score ≥80 as Interested")
- **Applied Tracking** - Track which jobs you've applied to
- **Application Stages** - Track progress: Applied → No Reply → Pending → Tech Test → Interview → Offer (or Ghosted/Rejected)
- **Stage History** - View timeline of stage changes for each application
- **Pipeline Kanban Board** - Drag-and-drop kanban board view of all applied jobs grouped by stage
- **Stale Application Alerts** - Visual badges on pipeline cards when applications have had no stage change beyond a configurable threshold
- **Follow-up Reminders** - Set per-job follow-up dates with due indicators and a count badge on the Pipeline tab
- **Cover Letter Templates** - Reusable templates with `{Company}`, `{Title}`, `{Location}` placeholders that auto-fill when applied to a job
- **Auto-Archive** - Automatically archive rejected/ghosted jobs after a configurable number of days
- **Auto-Delete** - Automatically delete old jobs after configurable days:
  - Delete unsuitable jobs after X days (default: 90)
  - Delete rejected jobs after X days (default: 60)
  - Delete ghosted jobs after X days (default: 60)
  - Set to 0 to disable auto-deletion for that type

### Application Stats Dashboard
- **Conversion Funnel** - Visual funnel showing progression from Applied through to Offer/Rejected
- **Pipeline Health** - Average days to reply, average days to interview, stale count, follow-ups due
- **Success & Ghosted Rates** - Percentage breakdowns of application outcomes
- **Weekly Activity Charts** - Jobs added and applications submitted over the last 12 weeks

### Rules Engine
- **Auto-classification Rules** - Automatically set interest, suitability, and remote status based on configurable rules
- **Rule Management** - Create and manage rules from the Rules page (`/rules`)
- **Score-Based Rules** - Use ML suitability scores in rules with numeric comparisons (>, >=, <, <=, =)
  - Example: Auto-mark jobs with score ≥80 as "Interested"
  - Example: Auto-reject jobs with score <30 as "Unsuitable"
  - Combine score conditions with other criteria (AND/OR logic)
- **Compound Conditions** - Create complex rules with multiple conditions
- **Priority System** - Rules execute in priority order (higher priority first)
- **Rule Statistics** - Track how many times each rule has triggered

### ML Job Scoring (Similar to JobSync)
- **Automatic Scoring** - Jobs are automatically scored 0-100 based on your preferences and behavior
- **Multi-Factor Analysis**:
  - **Skills Match** (25 points) - Matches against your preferred skills
  - **Salary Match** (20 points) - Scores based on your desired salary range
  - **Remote Preference** (15 points) - Preference for remote vs on-site
  - **Location Match** (10 points) - Matches against preferred locations
  - **Keyword Matching** (15 points) - Must-have and avoid keywords
  - **Company Preferences** (10 points) - Preferred and avoided companies
  - **Behavioral Learning** (15 points) - Learns from jobs you marked as interesting or applied to
- **Configurable Weights** - Adjust the importance of each factor (0-1 scale)
- **Visual Score Badges** - Color-coded badges on job cards:
  - 🟢 Green (80-100): Excellent match
  - 🔵 Blue (60-79): Good match
  - 🟡 Yellow (40-59): Fair match
  - ⚫ Grey (20-39): Poor match
  - 🔴 Red (0-19): Very poor match
- **Smart Recalculation** - Recalculate all scores with live progress indicator
  - Automatically skips unsuitable jobs
  - Shows progress bar and percentage
  - Runs in background without blocking UI
- **Min Score Filter** - Hide jobs below a certain score threshold
- **Integration with Rules** - Use scores in automation rules for smart job triage

### Background Crawling
- **Server-side Crawling** - Crawl job sites directly from the server without browser extensions
- **Supported Crawl Sites** - LinkedIn, S1Jobs, Welcome to the Jungle, and EnergyJobSearch
- **Scheduled Crawling** - Automatic crawling via Hangfire (SQL Server mode) or LocalBackgroundService
- **Ghosted Detection** - Automatically marks old applications with no reply as Ghosted (configurable threshold)
- **No Reply Detection** - Automatically marks applied jobs as No Reply after configurable days
- **Availability Checks** - Background checks for job listing availability
- **Auto-Archive Job** - Background job to archive rejected/ghosted jobs after configurable days
- **Job Cleanup Job** - Automatically deletes old unsuitable, rejected, and ghosted jobs based on configurable thresholds
- **Email Notification Job** - Daily digest email for follow-ups due and stale applications
- **Configurable Thresholds** - No Reply, Ghosted, Stale, and Auto-Delete days are all configurable per-user in Settings

### Authentication (SQL Server mode)
- **User Accounts** - Login with cookie-based authentication and API key support
- **Two-Factor Authentication** - Optional 2FA setup and verification
- **Password Recovery** - Forgot password and reset password flows

### Appearance
- **Dark Mode** - Toggle dark/light theme with one click; persists across sessions via localStorage
- **Keyword Highlighting** - Configurable keyword highlighting in job descriptions

### Notifications
- **Email Notifications** - Daily digest emails for follow-ups due and stale applications
- **Per-User SMTP Configuration** - Each user configures their own SMTP settings in Settings page
- **No Configuration File Needed** - SMTP settings are managed entirely through the UI (no appsettings.json required)
- **Password Reset Emails** - Uses recipient's SMTP settings for password reset notifications

### Sharing & Export
- **WhatsApp Sharing** - Share job details directly to WhatsApp
- **CSV Export** - Export applied jobs to CSV from the Jobs page
- **History Export** - Export full or filtered history to CSV
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
   - `BrowserExtensions/LinkedInExtension/` - LinkedIn
   - `BrowserExtensions/IndeedExtension/` - Indeed
   - `BrowserExtensions/S1JobsExtension/` - S1Jobs
   - `BrowserExtensions/WTTJExtension/` - Welcome to the Jungle
   - `BrowserExtensions/EnergyJobSearchExtension/` - EnergyJobSearch
5. Repeat for each extension you want to install

## Usage

### Extracting Jobs

1. **Start the Blazor app** - Make sure it's running (`dotnet run`)
2. **Navigate to a job site** - Go to LinkedIn Jobs, Indeed, S1Jobs, WTTJ, or EnergyJobSearch
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

   // EnergyJobSearch
   EJS.autoFetch(3)
   ```

2. **Via Extension Popup** - Click the extension icon and press "Auto-Fetch Descriptions"

3. **Stop Auto-Fetch** - Click the Stop button in the UI or run:
   ```javascript
   LJE.stopAutoFetch()  // or IND, S1J, WTTJ, EJS
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

Replace `XXX` with: `LJE` (LinkedIn), `IND` (Indeed), `S1J` (S1Jobs), `WTTJ` (Welcome to the Jungle), or `EJS` (EnergyJobSearch)

### Managing Jobs in the Web App

Navigate to `https://localhost:7046` to access the dashboard.

#### Pages

| Page | Path | Description |
|------|------|-------------|
| Jobs Dashboard | `/` or `/jobs` | Main job listing and management with AI analysis |
| Add Job | `/jobs/add` | Manually add a job |
| Dashboard | `/dashboard` | Application stats, conversion funnel, and weekly activity |
| Rules | `/rules` | Manage auto-classification rules (including ML score-based rules) |
| Settings | `/settings` | Configure user profile, 2FA, browser extension API key, job site URLs, **AI Assistant**, ML scoring preferences, highlight keywords, pipeline thresholds, auto-archive/delete settings, SMTP/email settings, and cover letter templates |
| History | `/history` | View audit log of changes with CSV export |
| Background Jobs | `/background-jobs` | Monitor background crawl jobs (LocalMode only) |
| Extension Install | `/extension-install` | Extension installation guide |
| About | `/about` | Application information |

#### Tabs
- **Browse** - New jobs that haven't been categorized
- **Possible** - Jobs marked as potentially suitable
- **Applied** - Jobs you've applied to (list view)
- **Pipeline** - Kanban board of applied jobs grouped by stage (drag-and-drop)
- **Archived** - Jobs that have been archived (manually or auto-archived)
- **Unsuitable** - Jobs that don't match your criteria

#### Job Cards
- Click the card header to open the job on the original site
- Use thumbs up/down buttons to set interest
- Use Possible/Unsuitable buttons to categorize
- Click "Applied" to track applications
- Use the stage dropdown to update application progress
- Pin/unpin jobs to keep them at the top of any tab
- Archive/unarchive jobs
- Click WhatsApp icon to share
- Click "View Full Description" for details
- Click "Fetch Details" to refresh job information from the job page (description, salary/location if available; some sites may limit what can be fetched server-side)

## Contacts

JobTracker includes a built-in contact manager so you can track recruiters, hiring managers, and your interactions.

### Where contacts live

- **Contacts page** (`/contacts`): browse/search all contacts and view interaction history.
- **Per-job Contacts tab**: each job has a **Contacts** tab in the job details modal where you can add/edit/remove contacts and log interactions (Email/Phone/LinkedIn/etc.).

### Capturing contacts from job sites

Contacts can be associated with jobs in two ways:

1. **Manual entry (always works):** open a job → **Contacts** tab → **Add Contact**.
2. **Automatic capture (via browser extensions where supported):** extensions can send recruiter/contact data along with description updates.

When an extension updates a job via `PUT /api/jobs/description`, it may include a `Contacts` array. The server will merge/dedupe contacts by name and link them to the matching job.

#### Filtering Options

| Filter | Description |
|--------|-------------|
| Search | Search in title, description, company, notes, and cover letter |
| Title Only | Search only in job titles |
| Location | Filter by job location |
| Job Type | Full-time, Part-time, Contract, etc. |
| Remote | Remote only or On-site only |
| Source | LinkedIn, Indeed, S1Jobs, WTTJ, or EnergyJobSearch |
| Interest | Interested, Not Interested, or Not Rated |
| Salary Info | Has salary or No salary listed |
| Salary Contains | Search within salary text (e.g., "100K") |
| Salary Target | Filter jobs whose parsed salary range covers the target value |
| Skill | Filter by skill with autocomplete |
| Stage | Filter applied jobs by application stage |
| Date Range | Filter by date posted |
| Sort By | Sort by date added, date posted, title, company, or salary |

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
| `Users` | User accounts and authentication |
| `JobListings` | All tracked jobs (with parsed salary, follow-up dates) |
| `HistoryEntries` | Audit log of changes |
| `JobRules` | Auto-classification rules |
| `AppSettings` | Per-user config (site URLs, rule settings, pipeline thresholds, dark mode, email, auto-archive, cover letter templates) |

## Project Structure

```
JobTracker/
├── Components/
│   ├── Pages/
│   │   ├── Jobs.razor              # Main job dashboard (browse, filter, kanban pipeline)
│   │   ├── AddJob.razor            # Manual job entry
│   │   ├── Dashboard.razor         # Application stats and conversion funnel
│   │   ├── Rules.razor             # Auto-classification rules
│   │   ├── Settings.razor          # App settings
│   │   ├── History.razor           # Change audit log with CSV export
│   │   ├── BackgroundJobs.razor    # Background job monitoring
│   │   └── ExtensionInstall.razor  # Extension setup guide
│   └── Layout/
│       └── NavMenu.razor           # Navigation menu
├── Models/
│   ├── JobListing.cs               # Job data model (with salary parsing, follow-up dates)
│   ├── AppSettings.cs              # Settings, site URLs, pipeline thresholds, filter presets, cover letter templates
│   ├── JobHistory.cs               # History entry model
│   └── JobRule.cs                  # Rule model
├── Services/
│   ├── JobListingService.cs        # Job storage, filtering, pipeline stats
│   ├── SalaryParser.cs             # Salary string parser (min/max extraction)
│   ├── JobScoringService.cs        # ML-based job scoring engine (0-100 scoring)
│   ├── AIJobAssistantService.cs    # 🤖 AI-powered job analysis (OpenAI & Claude)
│   ├── JobCrawlService.cs          # Server-side site crawling
│   ├── JobCrawlJob.cs              # Scheduled crawl job
│   ├── GhostedCheckJob.cs          # Auto-ghosted detection (configurable threshold)
│   ├── NoReplyCheckJob.cs          # Auto no-reply detection (configurable threshold)
│   ├── AvailabilityCheckJob.cs     # Job availability checks
│   ├── AutoArchiveJob.cs           # Auto-archive rejected/ghosted jobs
│   ├── JobCleanupJob.cs            # Auto-delete old unsuitable/rejected/ghosted jobs
│   ├── EmailNotificationJob.cs     # Daily digest email notifications
│   ├── EmailService.cs             # SMTP email sending
│   └── LocalBackgroundService.cs   # Background task runner
├── Data/
│   └── jobs.json                   # Persistent job storage (JSON mode)
├── BrowserExtensions/
│   ├── LinkedInExtension/
│   │   ├── manifest.json
│   │   ├── content.js
│   │   ├── popup.html
│   │   └── popup.js
│   ├── IndeedExtension/
│   │   ├── manifest.json
│   │   ├── content.js
│   │   ├── popup.html
│   │   └── popup.js
│   ├── S1JobsExtension/
│   │   ├── manifest.json
│   │   ├── content.js
│   │   ├── popup.html
│   │   └── popup.js
│   ├── WTTJExtension/
│   │   ├── manifest.json
│   │   ├── content.js
│   │   ├── popup.html
│   │   └── popup.js
│   └── EnergyJobSearchExtension/
│       ├── manifest.json
│       ├── content.js
│       ├── popup.html
│       └── popup.js
└── Program.cs                      # App configuration and API endpoints
```

## Configuration

### 🤖 AI Job Assistant Setup

#### 1. Get an API Key

**Option A: OpenAI**
- Visit [OpenAI API Keys](https://platform.openai.com/api-keys)
- Create a new API key (starts with `sk-`)
- Cost: ~$0.01-0.05 per job analysis depending on model

**Option B: Anthropic Claude**
- Visit [Anthropic Console](https://console.anthropic.com/settings/keys)
- Create a new API key (starts with `sk-ant-`)
- Cost: ~$0.008-0.08 per job analysis depending on model

#### 2. Configure in Settings

1. Navigate to **Settings** → **AI Job Assistant** section
2. **Enable AI Assistant** (check the box)
3. **Select AI Provider**: OpenAI or Claude
4. **Enter your API Key**
5. **Choose Model**:
   - **OpenAI**: GPT-3.5 Turbo (fast/cheap), GPT-4 (accurate), GPT-4o (latest)
   - **Claude**: Claude 3 Haiku (fast/cheap), Claude 3.5 Sonnet (recommended), Claude 3 Opus (most capable)
6. **Configure Your Profile**:
   - **Your Skills** - Comma-separated (e.g., "C#, .NET, Azure, React, TypeScript")
   - **Experience Summary** - Brief description of your experience
7. **Enable AI Features**:
   - ✅ Auto-analyze new jobs
   - ✅ Auto-generate cover letter suggestions
   - ✅ Show skill gap analysis
   - ✅ Show similar job recommendations
8. **Save AI Settings**

#### 3. Using AI Features

**On Job Cards:**
- Click **🤖 Analyze with AI** button to analyze any job
- Results appear in a beautiful modal with tabbed sections
- All insights are automatically saved to the job

**AI Analysis Modal:**
- 📊 **Summary** - 2-3 sentence job overview
- ✅ **Responsibilities** - Key duties extracted
- ⚙️ **Required Skills** - Color-coded (green = you have it)
- 🎓 **Qualifications** - Education and experience needs
- ⭐ **Nice-to-Have Skills** - Optional bonus skills
- 📈 **Skill Match** - Percentage with progress bar

**Advanced Features (from modal):**
- **📋 Interview Prep** - Get likely interview questions
- **💰 Salary Tips** - Negotiation strategies and scripts
- **📄 Resume Tips** - ATS keywords and optimization
- **📊 Success Prediction** - AI-powered success probability
- **🔍 Similar Jobs** - Find related opportunities
- **✉️ Cover Letter** - Personalized writing suggestions

#### AI Provider Comparison

| Feature | OpenAI | Claude |
|---------|--------|--------|
| **Best For** | Code, structured output | Analysis, writing, reasoning |
| **Context Window** | 8K-128K tokens | 200K tokens |
| **Pricing** | Moderate | Competitive |
| **Recommended Model** | GPT-3.5 Turbo or GPT-4o | Claude 3.5 Sonnet |
| **Cost per Analysis** | $0.01-0.05 | $0.008-0.08 |

**Tip**: Start with GPT-3.5 Turbo or Claude 3.5 Sonnet for best balance of quality and cost!

---

## 🎯 AI Features Showcase

### 📊 Job Analysis
Extract structured information from any job posting:
```
✅ Summary: "Senior .NET Developer role focusing on cloud architecture..."
✅ Responsibilities: ["Design scalable APIs", "Lead technical reviews"...]
✅ Required Skills: ["C#", ".NET Core", "Azure", "SQL Server"...]
✅ Qualifications: ["Bachelor's in CS", "5+ years .NET experience"...]
✅ Nice-to-Have: ["Docker", "Kubernetes", "React"...]
```

### 🎯 Skill Gap Analysis
Visual comparison with percentage match:
```
Match: 85% ✅

Skills You Have (7):
✅ C#  ✅ .NET  ✅ Azure  ✅ SQL Server  ✅ REST APIs  ✅ Git  ✅ Agile

Missing Required Skills (2):
❌ Kubernetes  ❌ Terraform

Bonus Skills You Have (2):
⭐ Docker  ⭐ React
```

### 📝 Cover Letter Assistant
AI generates personalized suggestions:
```
Opening: "As a passionate .NET architect with 8 years of cloud experience..."

Selling Points:
• Extensive Azure expertise aligns with your cloud-first approach
• Proven track record leading teams of 5+ developers
• Strong background in microservices and container orchestration

Closing: "I'm excited to bring my technical leadership to [Company]..."
```

### 💼 Interview Preparation
Get likely questions before the interview:
```
Technical Questions:
1. How would you design a microservices architecture on Azure?
2. Explain your approach to optimizing database queries in SQL Server
3. Describe a complex technical challenge you solved recently

Behavioral Questions:
1. Tell me about a time you had to mentor junior developers
2. How do you handle conflicting priorities in agile sprints?

Questions to Ask Them:
1. What's the team's approach to DevOps and CI/CD?
2. How does the company support professional development?
```

### 💰 Salary Negotiation
Market data and professional scripts:
```
Market Rate: £60,000-£75,000 for Senior .NET Developer in London

Strategies:
• Research shows market rate 15% above initial offer
• Emphasize your Azure certifications and leadership experience
• Focus on total compensation including benefits

Opening Statement:
"I'm very excited about this opportunity. Based on my research and 
the value I bring, I was hoping we could discuss £70,000..."
```

### 📄 Resume Optimization
ATS keywords and tailored content:
```
Keywords to Include:
"microservices", "Azure DevOps", "CI/CD", "Agile", "Scrum Master"

Skills to Highlight:
1. Azure Architecture (Solution Architect certified)
2. .NET Core 6/7 (8+ years)
3. Team Leadership (Led teams of 5+)

Tailored Summary:
"Senior .NET Developer with 8 years of cloud-native development, 
specializing in Azure microservices and team leadership..."
```

### 🔮 Success Prediction
AI-powered probability estimate:
```
Success Probability: 78% 📈

Strength Factors:
✅ Strong Azure experience matches requirements
✅ Leadership background aligns with senior role
✅ Technical skills cover 85% of requirements

Risk Factors:
⚠️ No Kubernetes experience (required skill)
⚠️ Company prefers finance sector background

Recommendation: Apply Now ✅
Tip: Highlight transferable skills to address Kubernetes gap
```

### 🔍 Similar Jobs
AI finds related opportunities:
```
Similar Jobs Found: 5

1. Cloud Solutions Architect @ TechCorp (92% match)
   Why: 8 shared skills, similar role, remote

2. Senior .NET Developer @ DataSys (87% match)
   Why: Same stack, both emphasize Azure, London-based

3. Technical Lead @ CloudFirst (82% match)
   Why: Leadership focus, cloud-native, remote
```

---

### ML Job Scoring Setup

1. **Navigate to Settings** - Go to `/settings` or click Settings in the sidebar
2. **Scroll to "Job Scoring / ML Preferences"** section
3. **Enable ML-based Job Scoring** - Check the checkbox
4. **Configure Scoring Weights** (0 = disabled, 1 = full weight):
   - Skills Match Weight
   - Salary Match Weight
   - Remote Preference Weight
   - Location Weight
   - Keywords Weight
   - Company Preference Weight
   - Learning from Behavior Weight
5. **Set Your Preferences**:
   - **Preferred Skills** - Comma-separated (e.g., "C#, .NET, Azure, React")
   - **Salary Range** - Minimum and maximum desired salary
   - **Remote Preference** - Check if you prefer remote jobs
   - **Preferred Locations** - Comma-separated locations
   - **Must-Have Keywords** - Keywords that should be in job descriptions
   - **Avoid Keywords** - Keywords that indicate jobs you want to skip
   - **Preferred Companies** - Companies you'd like to work for
   - **Avoid Companies** - Companies to skip
   - **Minimum Score to Show** - Hide jobs below this threshold (0 = show all)
6. **Save Scoring Preferences**
7. **Recalculate All Scores** - Click to score all existing jobs (with live progress)
8. **View Scores** - Go to Jobs page to see score badges on each job

### Score-Based Rules

Create automation rules using ML scores:

1. Go to **Rules** page
2. Click **Add New Rule**
3. Select **Field**: `Suitability Score`
4. Select **Operator**: `>=`, `>`, `<=`, `<`, or `=`
5. Enter **Value**: Score threshold (0-100)
6. Set **Action**: Set Interest, Suitability, or IsRemote
7. **Example Rules**:
   - Score >= 80 → Set Interest = Interested
   - Score < 30 → Set Suitability = Unsuitable
   - Score >= 70 AND Location Contains "Remote" → Set Interest = Interested

### Email Configuration

1. **Navigate to Settings** → **Email / SMTP** section
2. **Configure SMTP Settings**:
   - SMTP Host (e.g., smtp.gmail.com)
   - Port (e.g., 587)
   - Username and Password
   - From Email and From Name
3. **Enable Email Notifications** (optional)
4. **Configure Notification Preferences**:
   - Enable daily email digest
   - Follow-ups due notifications
   - Stale applications notifications

**Note**: Each user manages their own SMTP settings. There is no shared SMTP configuration.

### Auto-Delete Configuration

1. **Navigate to Settings** → **Auto-Archive & Auto-Delete** section
2. **Configure Delete Thresholds**:
   - **Delete Unsuitable** - Days to keep unsuitable jobs (0 = don't delete)
   - **Delete Rejected** - Days to keep rejected applications (0 = don't delete)
   - **Delete Ghosted** - Days to keep ghosted applications (0 = don't delete)
3. **Save Settings**
4. Background job will automatically delete old jobs based on these thresholds

## Troubleshooting

### Extension not detecting jobs

1. Make sure you're on a job listing page
2. Open DevTools (F12) → Console tab
3. Look for `[LJE]`, `[IND]`, `[S1J]`, `[WTTJ]`, or `[EJS]` messages
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

---

## 🛠️ Tech Stack

### Backend
- **.NET 10** - Latest .NET framework
- **Blazor Server** - Interactive server-side rendering
- **C# 14** - Modern C# language features
- **SQL Server** - Optional enterprise database support
- **Hangfire** - Background job processing (SQL Server mode)
- **Entity Framework Core** - ORM for SQL Server

### AI & ML
- **OpenAI GPT** - GPT-3.5 Turbo, GPT-4, GPT-4o support
- **Anthropic Claude** - Claude 3 Haiku, Sonnet, Opus support
- **Custom ML Scoring** - Multi-factor job scoring algorithm
- **JSON Deserialization** - Structured AI response parsing

### Frontend
- **Blazor Components** - Reusable UI components
- **Bootstrap 5** - Responsive design system
- **Bootstrap Icons** - Icon library
- **Dark Mode** - Theme switching with localStorage
- **Drag & Drop** - Kanban board (SortableJS integration)

### Browser Extensions
- **Chrome Extension API** - Manifest V3
- **JavaScript** - Content scripts and background workers
- **DOM Manipulation** - Job extraction from various sites
- **Cross-Origin Messaging** - Extension to app communication

### Data Storage
- **JSON Files** - Default lightweight storage
- **SQL Server** - Enterprise-grade option
- **Entity Framework Migrations** - Schema management
- **Dual Backend Architecture** - Seamless switching

### Authentication & Security
- **Cookie Authentication** - Session management
- **API Key Support** - Extension authentication
- **TOTP 2FA** - Time-based one-time passwords
- **Password Hashing** - Secure credential storage
- **SMTP Email** - Password reset and notifications

### Background Jobs
- **LocalBackgroundService** - JSON mode background tasks
- **Hangfire** - SQL Server mode job scheduling
- **Recurring Jobs** - Crawling, email, cleanup
- **CRON Scheduling** - Flexible job timing

---

## 📊 Stats & Metrics

- **9 AI Features** - Comprehensive job search assistance
- **2 AI Providers** - OpenAI and Anthropic Claude
- **8 Model Options** - From fast/cheap to most capable
- **5 Job Sites** - LinkedIn, Indeed, S1Jobs, WTTJ, EnergyJobSearch
- **7 Application Stages** - Full pipeline tracking
- **ML Scoring** - 7-factor intelligent job matching
- **Kanban Pipeline** - Visual drag-and-drop board
- **Background Jobs** - 7 automated tasks
- **Dual Storage** - JSON or SQL Server
- **Browser Extensions** - 5 dedicated extractors

---

## 🎉 Why JobTracker?

✅ **AI-Powered** - Leverage GPT-4 or Claude to work smarter  
✅ **Comprehensive** - Track every aspect of your job search  
✅ **Intelligent** - ML scoring learns from your preferences  
✅ **Automated** - Rules engine and background jobs do the work  
✅ **Visual** - Kanban boards, charts, and color-coded indicators  
✅ **Flexible** - Choose your AI provider, storage, and workflows  
✅ **Private** - Your data stays on your server  
✅ **Extensible** - Open architecture for customization  

---

## License

This project is for personal use to track job applications.

## Contributing

Feel free to submit issues and enhancement requests!
