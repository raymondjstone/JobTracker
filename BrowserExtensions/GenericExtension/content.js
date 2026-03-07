(function() {
  var SERVER_URL = 'https://localhost:7046';
  var API_KEY = '';
  var totalSent = 0;
  var totalDuplicates = 0;
  var sending = false;
  var sentJobIds = {};
  var uiCreated = false;
  var lastUrl = window.location.href;
  var extractionInterval = null;
  var activated = false;

  // Check RFC 1918 private IP ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
  function isPrivateIP(host) {
    if (host.startsWith('10.')) return true;
    if (host.startsWith('192.168.')) return true;
    if (host.startsWith('172.')) {
      var second = parseInt(host.split('.')[1], 10);
      if (second >= 16 && second <= 31) return true;
    }
    return false;
  }

  // Skip localhost, 127.0.0.1, and internal pages — never extract from the Job Tracker app itself
  var currentHost = window.location.hostname;
  if (currentHost === 'localhost' || currentHost === '127.0.0.1' || isPrivateIP(currentHost) ||
      currentHost === '[::1]' ||
      window.location.protocol === 'chrome-extension:' || window.location.protocol === 'edge:' ||
      window.location.protocol === 'chrome:' || window.location.protocol === 'about:') {
    return; // Do not run on local/internal pages
  }

  // Skip if a site-specific extension is already loaded.
  // Content scripts from different extensions run in isolated worlds and cannot
  // see each other's window properties. DOM attributes ARE shared, so check those.
  function siteSpecificExtensionLoaded() {
    var root = document.documentElement;
    return root.hasAttribute('data-lje-installed') ||
           root.hasAttribute('data-ind-installed') ||
           root.hasAttribute('data-s1j-installed') ||
           root.hasAttribute('data-wttj-installed') ||
           root.hasAttribute('data-ejs-installed');
  }
  if (siteSpecificExtensionLoaded()) return;

  console.log('[GEN] Universal Job Extractor loaded');

  // Load server URL and API key from storage
  if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
      if (result.serverUrl) {
        SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        console.log('[GEN] Using server URL from settings:', SERVER_URL);
      }
      if (result.apiKey) {
        API_KEY = result.apiKey;
        console.log('[GEN] API key loaded');
      }
      // Start detection after settings loaded
      startDetection();
    });
  } else {
    startDetection();
  }

  function getHeaders() {
    var headers = { 'Content-Type': 'application/json' };
    if (API_KEY) {
      headers['X-API-Key'] = API_KEY;
    }
    return headers;
  }

  // Route fetch through background service worker to avoid CORS issues.
  // Content scripts run in the page's origin (e.g. monster.com), but the
  // background script runs in chrome-extension:// which is CORS-allowed.
  function apiFetch(url, options) {
    return new Promise(function(resolve, reject) {
      if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.sendMessage) {
        chrome.runtime.sendMessage({ type: 'api', url: url, options: options }, function(response) {
          if (chrome.runtime.lastError) {
            reject(new Error(chrome.runtime.lastError.message));
            return;
          }
          if (!response) {
            reject(new Error('No response from background'));
            return;
          }
          if (response.error) {
            reject(new Error(response.error));
            return;
          }
          resolve(response);
        });
      } else {
        // Fallback to direct fetch (e.g. when testing without extension context)
        fetch(url, options)
          .then(function(r) { return r.json().then(function(d) { return { ok: r.ok, status: r.status, data: d }; }); })
          .then(resolve)
          .catch(reject);
      }
    });
  }

  // Mark that extension is installed
  window.GenericJobExtractorInstalled = true;
  document.documentElement.setAttribute('data-gen-installed', 'true');

  // Listen for messages from popup
  if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.onMessage) {
    chrome.runtime.onMessage.addListener(function(message, sender, sendResponse) {
      if (message.action === 'extract') {
        doExtract(true);
        sendResponse({ success: true });
      } else if (message.action === 'checkSchema') {
        sendResponse({
          detected: getAllJsonLdJobPostings().length > 0,
          cardCount: findJobCards().length
        });
      }
    });
  }

  function startDetection() {
    // Initial check after DOM settles
    setTimeout(function() {
      tryActivate();
    }, 2000);

    // Re-check after more time for slow SPAs
    setTimeout(function() {
      tryActivate();
    }, 5000);

    // Watch for SPA navigation (URL changes without page reload)
    setInterval(function() {
      if (window.location.href !== lastUrl) {
        console.log('[GEN] URL changed:', lastUrl, '->', window.location.href);
        lastUrl = window.location.href;
        // Reset for new page
        setTimeout(tryActivate, 2000);
      }
    }, 1000);

    // Watch for dynamically added JSON-LD scripts or job cards
    var observer = new MutationObserver(function(mutations) {
      var dominated = false;
      for (var i = 0; i < mutations.length; i++) {
        var added = mutations[i].addedNodes;
        for (var j = 0; j < added.length; j++) {
          var node = added[j];
          if (node.nodeType !== 1) continue;
          // Check if a JSON-LD script was added
          if (node.tagName === 'SCRIPT' && node.type === 'application/ld+json') {
            dominated = true;
            break;
          }
          // Check if it contains JSON-LD or links to job pages
          if (node.querySelector && (
            node.querySelector('script[type="application/ld+json"]') ||
            node.querySelector('a[href*="/job"]')
          )) {
            dominated = true;
            break;
          }
        }
        if (dominated) break;
      }
      if (dominated) {
        setTimeout(tryActivate, 1000);
      }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
  }

  function tryActivate() {
    // Re-check: a site-specific extension may have loaded after us
    if (siteSpecificExtensionLoaded()) {
      if (extractionInterval) { clearInterval(extractionInterval); extractionInterval = null; }
      var ui = document.getElementById('gen-ui');
      if (ui) ui.remove();
      console.log('[GEN] Site-specific extension detected, disabling universal extractor');
      return;
    }

    var jsonLdJobs = getAllJsonLdJobPostings();
    var domCards = findJobCards();

    if (jsonLdJobs.length > 0 || domCards.length > 0) {
      var method = jsonLdJobs.length > 0 ? 'schema.org' : 'DOM cards';
      var count = jsonLdJobs.length > 0 ? jsonLdJobs.length : domCards.length;
      console.log('[GEN] Found ' + count + ' job(s) via ' + method);

      if (!uiCreated) {
        createUI();
        uiCreated = true;
      }
      doExtract(false);

      // Set up periodic re-check if not already running
      if (!extractionInterval) {
        extractionInterval = setInterval(function() { doExtract(false); }, 8000);
      }
    }
  }

  // === JSON-LD Parsing ===

  function getJsonLdJobData() {
    var scripts = document.querySelectorAll('script[type="application/ld+json"]');
    for (var k = 0; k < scripts.length; k++) {
      try {
        var data = JSON.parse(scripts[k].textContent);
        var found = findJobPostingInData(data);
        if (found) return found;
      } catch (e) {}
    }
    return null;
  }

  function findJobPostingInData(data) {
    if (!data) return null;

    // Direct JobPosting
    if (data['@type'] === 'JobPosting') return data;

    // Array of items
    if (Array.isArray(data)) {
      for (var i = 0; i < data.length; i++) {
        if (data[i] && data[i]['@type'] === 'JobPosting') return data[i];
      }
    }

    // @graph container
    if (data['@graph'] && Array.isArray(data['@graph'])) {
      for (var g = 0; g < data['@graph'].length; g++) {
        if (data['@graph'][g] && data['@graph'][g]['@type'] === 'JobPosting') return data['@graph'][g];
      }
    }

    return null;
  }

  function getAllJsonLdJobPostings() {
    var results = [];
    var scripts = document.querySelectorAll('script[type="application/ld+json"]');
    for (var k = 0; k < scripts.length; k++) {
      try {
        var data = JSON.parse(scripts[k].textContent);
        collectJobPostings(data, results);
      } catch (e) {}
    }
    return results;
  }

  function collectJobPostings(data, results) {
    if (!data) return;

    if (data['@type'] === 'JobPosting') {
      results.push(data);
      return;
    }

    if (Array.isArray(data)) {
      for (var i = 0; i < data.length; i++) {
        if (data[i] && data[i]['@type'] === 'JobPosting') results.push(data[i]);
      }
      return;
    }

    if (data['@graph'] && Array.isArray(data['@graph'])) {
      for (var g = 0; g < data['@graph'].length; g++) {
        if (data['@graph'][g] && data['@graph'][g]['@type'] === 'JobPosting') results.push(data['@graph'][g]);
      }
    }
  }

  // === DOM Job Card Detection ===
  //
  // Strategy: Instead of trying to match specific card container selectors
  // (which are fragile and site-specific), we find all links that point to
  // individual job detail pages and extract context from the surrounding DOM.
  //
  // A "job detail link" is identified by URL pattern: it points to a path
  // containing /job/ or /jobs/ followed by an ID or slug, and is NOT the
  // current search results page.

  // Patterns that indicate a link points to an individual job detail page
  var JOB_URL_PATTERNS = [
    /\/jobs?\/view\//i,
    /\/jobs?\/[a-z0-9-]+\/[a-z0-9-]/i,    // /job/title-slug/id or /jobs/title/id
    /\/job\/[^/?#]+$/i,                     // /job/some-slug
    /\/jobs\/[^/?#]+$/i,                    // /jobs/some-slug
    /\/career[s]?\/[^/?#]+\/[^/?#]+/i,     // /careers/company/job-slug
    /\/position[s]?\/[^/?#]+/i,            // /positions/slug
    /\/opening[s]?\/[^/?#]+/i,             // /openings/slug
    /\/vacancy\/[^/?#]+/i,                 // /vacancy/slug
    /[?&](?:jobId|job_id|jk|vjk|id)=/i    // ?jobId=xxx or ?jk=xxx
  ];

  // URL patterns to exclude (navigation, auth, legal pages)
  var EXCLUDE_URL_PATTERNS = [
    /\/jobs\/?(?:\?|$)/i,          // bare /jobs/ or /jobs?query= (search results listing)
    /\/jobs\/category/i,
    /\/jobs\/location/i,
    /\/login/i, /\/register/i, /\/sign-?in/i, /\/sign-?up/i,
    /\/about/i, /\/contact/i, /\/privacy/i, /\/terms/i,
    /^javascript:/i,
    /^mailto:/i, /^tel:/i
  ];

  function isJobDetailUrl(href) {
    if (!href) return false;
    try {
      var url = new URL(href, window.location.origin);
      var fullUrl = url.href;
      // Never treat links to the Job Tracker server as job URLs
      if (fullUrl.startsWith(SERVER_URL)) return false;
      // Exclude localhost/internal links
      var h = url.hostname;
      if (h === 'localhost' || h === '127.0.0.1' || h === '[::1]' || isPrivateIP(h)) return false;
      // Must be same domain or well-known job aggregator
      if (url.hostname !== window.location.hostname &&
          !url.hostname.endsWith('.' + window.location.hostname.replace(/^www\./, ''))) {
        return false;
      }
      // Check exclusions
      for (var e = 0; e < EXCLUDE_URL_PATTERNS.length; e++) {
        if (EXCLUDE_URL_PATTERNS[e].test(fullUrl)) return false;
      }
      // Check positive patterns
      for (var p = 0; p < JOB_URL_PATTERNS.length; p++) {
        if (JOB_URL_PATTERNS[p].test(url.pathname + url.search)) return true;
      }
      return false;
    } catch (e) {
      return false;
    }
  }

  function findJobCards() {
    var results = [];
    var seenUrls = {};

    // Find all links that look like they point to individual job pages
    var allLinks = document.querySelectorAll('a[href]');
    for (var i = 0; i < allLinks.length; i++) {
      var link = allLinks[i];
      var href = link.href;
      if (!href || !isJobDetailUrl(href)) continue;

      // Normalize URL
      var normalUrl = normalizeJobUrl(href);
      if (seenUrls[normalUrl]) continue;
      seenUrls[normalUrl] = true;

      // The link text is likely the job title
      var title = cleanText(link.textContent);
      if (!title || title.length < 3 || title.length > 300) continue;

      // Skip if link text looks like navigation (very short or generic)
      if (/^(apply|view|details|more|see|click|learn|read|open|save|share|hide|close)$/i.test(title)) continue;

      // Find the card container: walk up the DOM to find the nearest
      // article, li, or div that contains this link but is a reasonable size
      var container = findCardContainer(link);

      results.push({
        link: link,
        url: normalUrl,
        title: title,
        container: container
      });
    }

    if (results.length > 0) {
      console.log('[GEN] Found ' + results.length + ' job links on page');
    }
    return results;
  }

  function findCardContainer(link) {
    // Walk up to find the nearest container that looks like a card
    var el = link.parentElement;
    var best = null;
    for (var depth = 0; depth < 8 && el; depth++) {
      var tag = el.tagName;
      // Article or LI are strong card indicators
      if (tag === 'ARTICLE' || tag === 'LI') {
        return el;
      }
      // A div/section with reasonable content is a good candidate
      if ((tag === 'DIV' || tag === 'SECTION') && el.textContent.length > 30 && el.textContent.length < 5000) {
        best = el;
        // Keep going up a bit to see if there's an article/li wrapper
      }
      el = el.parentElement;
    }
    return best || link.parentElement;
  }

  function extractJobFromCard(cardInfo) {
    var job = {
      Title: cardInfo.title,
      Company: '',
      Location: '',
      Description: '',
      JobType: 0,
      Salary: '',
      Url: cardInfo.url,
      DatePosted: new Date().toISOString(),
      IsRemote: false,
      Skills: [],
      Source: getSourceFromHostname(),
      Contacts: []
    };

    var container = cardInfo.container;
    if (!container) return job;

    // Extract text blocks from the container - skip the title itself
    var textParts = getTextParts(container, cardInfo.link);

    // Try to identify company, location from text parts
    // Heuristic: in most card layouts, the order is Title, Company, Location
    for (var i = 0; i < textParts.length && i < 6; i++) {
      var part = textParts[i];
      if (!part || part.length < 2 || part.length > 300) continue;

      // Skip if it's the title
      if (part === job.Title) continue;

      // Detect company (usually first non-title text, often short)
      if (!job.Company && part.length > 1 && part.length < 100 && !/^\d/.test(part) &&
          !looksLikeLocation(part) && !looksLikeSalary(part) && !looksLikeDate(part)) {
        job.Company = part;
        continue;
      }

      // Detect location
      if (!job.Location && looksLikeLocation(part)) {
        job.Location = part;
        continue;
      }

      // Detect salary
      if (!job.Salary && looksLikeSalary(part)) {
        job.Salary = part;
        continue;
      }
    }

    // Fallback: try data-testid based selectors within the container
    if (!job.Company) job.Company = getTextBySelector(container, [
      '[data-testid*="company"]', '[data-testid*="Company"]',
      '[class*="company"]', '[class*="Company"]',
      '[class*="employer"]', '[class*="Employer"]'
    ]);
    if (!job.Location) job.Location = getTextBySelector(container, [
      '[data-testid*="location"]', '[data-testid*="Location"]',
      '[class*="location"]', '[class*="Location"]'
    ]);

    // Check for remote indicators
    var cardText = container.textContent.toLowerCase();
    if (cardText.includes('remote') || cardText.includes('telecommute') || cardText.includes('work from home')) {
      job.IsRemote = true;
    }

    return job;
  }

  function getTextParts(container, skipEl) {
    // Get distinct text blocks from a container element.
    // Walk through child elements and collect text from leaf-ish nodes.
    var parts = [];
    var children = container.children;
    for (var i = 0; i < children.length; i++) {
      var child = children[i];
      if (child === skipEl || child.contains(skipEl)) {
        // Include deeper children that aren't the link itself
        var subParts = getTextParts(child, skipEl);
        parts = parts.concat(subParts);
        continue;
      }
      var text = cleanText(child.textContent);
      if (text && text.length > 0) {
        parts.push(text);
      }
    }
    return parts;
  }

  function getTextBySelector(container, selectors) {
    for (var i = 0; i < selectors.length; i++) {
      var el = container.querySelector(selectors[i]);
      if (el) {
        var text = cleanText(el.textContent);
        if (text && text.length > 1 && text.length < 200) return text;
      }
    }
    return '';
  }

  function looksLikeLocation(text) {
    // Common location patterns
    return /(?:remote|hybrid|on-?site)/i.test(text) ||
           /,\s*[A-Z]{2}(?:\s|$)/.test(text) ||                     // City, ST
           /(?:united states|united kingdom|canada|australia|germany|france|india)/i.test(text) ||
           /(?:new york|san francisco|london|berlin|paris|toronto|sydney|chicago|seattle|austin|boston|denver|portland|dallas|houston|atlanta|phoenix|remote)/i.test(text);
  }

  function looksLikeSalary(text) {
    return /(?:\$|£|€|USD|GBP|EUR)\s*[\d,]+/i.test(text) ||
           /[\d,]+\s*(?:\/|per)\s*(?:year|yr|annum|month|hour|hr)/i.test(text) ||
           /(?:salary|compensation|pay)\s*[:]/i.test(text);
  }

  function looksLikeDate(text) {
    return /^\d+[dh]?\s*ago$/i.test(text) ||
           /^(?:today|yesterday|just now)/i.test(text) ||
           /^\d{1,2}\s*(?:day|hour|minute|week|month)/i.test(text);
  }

  function normalizeJobUrl(href) {
    try {
      var url = new URL(href);
      // Remove common tracking params
      var removeParams = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_content',
                          'utm_term', 'ref', 'src', 'from', 'source', 'fbclid',
                          'gclid', 'mc_cid', 'mc_eid', 'so', 'recency'];
      removeParams.forEach(function(p) { url.searchParams.delete(p); });
      return url.href;
    } catch (e) {
      return href;
    }
  }

  // === Field Extraction from JSON-LD ===

  function extractJobFromJsonLd(data) {
    var job = {
      Title: data.title || '',
      Company: '',
      Location: '',
      Description: '',
      JobType: 0,
      Salary: '',
      Url: getCurrentJobUrl(),
      DatePosted: data.datePosted || new Date().toISOString(),
      IsRemote: false,
      Skills: [],
      Source: getSourceFromHostname(),
      Contacts: []
    };

    // Company
    if (data.hiringOrganization) {
      if (typeof data.hiringOrganization === 'string') {
        job.Company = data.hiringOrganization;
      } else if (data.hiringOrganization.name) {
        job.Company = data.hiringOrganization.name;
      }
    }

    // Location
    job.Location = extractLocation(data);

    // Description
    if (data.description) {
      job.Description = stripHtml(data.description);
    }

    // Employment type → JobType
    if (data.employmentType) {
      job.JobType = mapEmploymentType(data.employmentType);
    }

    // Salary
    job.Salary = extractSalary(data);

    // Remote
    if (data.jobLocationType) {
      var locType = Array.isArray(data.jobLocationType) ? data.jobLocationType : [data.jobLocationType];
      job.IsRemote = locType.some(function(t) {
        return t && t.toUpperCase() === 'TELECOMMUTE';
      });
    }

    // Also check location text for remote
    if (!job.IsRemote && job.Location && /remote|telecommute/i.test(job.Location)) {
      job.IsRemote = true;
    }

    // Skills
    if (data.skills) {
      if (typeof data.skills === 'string') {
        job.Skills = data.skills.split(',').map(function(s) { return s.trim(); }).filter(Boolean);
      } else if (Array.isArray(data.skills)) {
        job.Skills = data.skills.map(function(s) { return typeof s === 'string' ? s.trim() : ''; }).filter(Boolean);
      }
    }

    // Contacts
    var contacts = extractContacts(data);
    if (contacts.length > 0) {
      job.Contacts = contacts;
    }

    // URL from JSON-LD if available
    if (data.url) {
      job.Url = data.url;
    }

    return job;
  }

  function extractLocation(data) {
    if (!data.jobLocation) return '';

    var locations = Array.isArray(data.jobLocation) ? data.jobLocation : [data.jobLocation];
    var parts = [];

    for (var i = 0; i < locations.length; i++) {
      var loc = locations[i];
      if (typeof loc === 'string') {
        parts.push(loc);
        continue;
      }
      if (loc.address) {
        var addr = loc.address;
        if (typeof addr === 'string') {
          parts.push(addr);
        } else {
          var addrParts = [];
          if (addr.addressLocality) addrParts.push(addr.addressLocality);
          if (addr.addressRegion) addrParts.push(addr.addressRegion);
          if (addr.addressCountry) {
            var country = typeof addr.addressCountry === 'string' ? addr.addressCountry :
                          (addr.addressCountry.name || '');
            if (country) addrParts.push(country);
          }
          if (addrParts.length > 0) parts.push(addrParts.join(', '));
        }
      } else if (loc.name) {
        parts.push(loc.name);
      }
    }

    return parts.join('; ');
  }

  function extractSalary(data) {
    if (!data.baseSalary) return '';

    var salary = data.baseSalary;
    if (typeof salary === 'string') return salary;

    var value = salary.value;
    if (!value) return '';

    if (typeof value === 'object') {
      var min = value.minValue || value.value;
      var max = value.maxValue;
      var unit = value.unitText || salary.unitText || '';
      var currency = salary.currency || '';

      var parts = [];
      if (currency) parts.push(currency);
      if (min && max && min !== max) {
        parts.push(min + ' - ' + max);
      } else if (min) {
        parts.push(String(min));
      }
      if (unit) parts.push('per ' + unit.toLowerCase());
      return parts.join(' ');
    }

    return String(value);
  }

  function mapEmploymentType(type) {
    if (Array.isArray(type)) type = type[0];
    if (!type) return 0;

    var t = type.toUpperCase().replace(/[_\s-]/g, '');
    if (t === 'FULLTIME') return 0;
    if (t === 'PARTTIME') return 1;
    if (t === 'CONTRACT' || t === 'CONTRACTOR') return 2;
    if (t === 'TEMPORARY' || t === 'TEMP') return 3;
    if (t === 'INTERN' || t === 'INTERNSHIP') return 4;
    if (t === 'VOLUNTEER') return 5;
    return 0;
  }

  function extractContacts(data) {
    var contacts = [];

    if (data.applicationContact) {
      var ac = data.applicationContact;
      if (ac.name || ac.email) {
        var contact = {};
        if (ac.name) contact.Name = ac.name;
        if (ac.email) contact.Email = ac.email;
        if (ac.telephone) contact.Phone = ac.telephone;
        if (ac.url) contact.ProfileUrl = ac.url;
        contacts.push(contact);
      }
    }

    if (data.hiringOrganization && data.hiringOrganization.contactPoint) {
      var cp = data.hiringOrganization.contactPoint;
      if (cp.name || cp.contactType || cp.email) {
        var cpContact = {};
        cpContact.Name = cp.name || cp.contactType || '';
        if (cp.email) cpContact.Email = cp.email;
        if (cp.telephone) cpContact.Phone = cp.telephone;
        if (cp.url) cpContact.ProfileUrl = cp.url;
        contacts.push(cpContact);
      }
    }

    return contacts;
  }

  // === Heuristic Fallback (single job page) ===

  function extractHeuristic() {
    console.log('[GEN] Attempting heuristic extraction (no schema.org found)');
    var job = {
      Title: '',
      Company: '',
      Location: '',
      Description: '',
      JobType: 0,
      Salary: '',
      Url: getCurrentJobUrl(),
      DatePosted: new Date().toISOString(),
      IsRemote: false,
      Skills: [],
      Source: getSourceFromHostname(),
      Contacts: []
    };

    // Try OpenGraph meta tags
    var ogTitle = document.querySelector('meta[property="og:title"]');
    if (ogTitle && ogTitle.content) {
      job.Title = ogTitle.content.trim();
    }

    var ogSiteName = document.querySelector('meta[property="og:site_name"]');
    if (ogSiteName && ogSiteName.content) {
      if (!job.Company) job.Company = ogSiteName.content.trim();
    }

    // Try parsing page title: "Job Title - Company | Site" or "Job Title at Company"
    if (!job.Title) {
      var pageTitle = document.title || '';
      var titlePatterns = [
        /^(.+?)\s*[-|]\s*(.+?)\s*[-|]\s*.+$/,
        /^(.+?)\s+at\s+(.+?)(?:\s*[-|].+)?$/i,
        /^(.+?)\s*[-|]\s*(.+)$/
      ];
      for (var p = 0; p < titlePatterns.length; p++) {
        var m = pageTitle.match(titlePatterns[p]);
        if (m) {
          if (!job.Title) job.Title = m[1].trim();
          if (!job.Company || job.Company === (ogSiteName && ogSiteName.content)) {
            job.Company = m[2].trim();
          }
          break;
        }
      }
      if (!job.Title) job.Title = pageTitle;
    }

    // Try to find description from largest text block with job keywords
    var jobKeywords = /responsibilities|requirements|qualifications|experience|skills|what you.ll|about the role|job description|who we.re looking for/i;
    var blocks = document.querySelectorAll('div, section, article, main');
    var bestBlock = null;
    var bestScore = 0;

    for (var b = 0; b < blocks.length; b++) {
      var text = blocks[b].innerText || '';
      if (text.length < 200 || text.length > 50000) continue;
      if (!jobKeywords.test(text)) continue;

      var score = text.length;
      var matches = text.match(new RegExp(jobKeywords.source, 'gi'));
      if (matches) score += matches.length * 500;
      if (text.length > 10000) score -= (text.length - 10000);

      if (score > bestScore) {
        bestScore = score;
        bestBlock = text;
      }
    }

    if (bestBlock) {
      job.Description = bestBlock.substring(0, 15000).trim();
    }

    // Check remote
    var pageText = document.body ? document.body.textContent.toLowerCase() : '';
    if (pageText.includes('remote') || pageText.includes('work from home')) {
      job.IsRemote = true;
    }

    if (!job.Title || job.Title.length < 3) {
      console.log('[GEN] Heuristic extraction failed - could not determine job title');
      return null;
    }

    console.log('[GEN] Heuristic extraction found: "' + job.Title + '" at "' + job.Company + '"');
    return job;
  }

  // === Utility Functions ===

  function getCurrentJobUrl() {
    var canonical = document.querySelector('link[rel="canonical"]');
    if (canonical && canonical.href) {
      return canonical.href;
    }
    var url = window.location.href;
    try {
      var u = new URL(url);
      var keepParams = ['id', 'jobId', 'jk', 'vjk', 'job_id', 'q', 'where'];
      var newParams = new URLSearchParams();
      keepParams.forEach(function(p) {
        if (u.searchParams.has(p)) newParams.set(p, u.searchParams.get(p));
      });
      var cleaned = u.origin + u.pathname;
      var qs = newParams.toString();
      if (qs) cleaned += '?' + qs;
      return cleaned;
    } catch (e) {
      return url;
    }
  }

  function getSourceFromHostname() {
    try {
      var hostname = window.location.hostname.replace(/^www\./, '');
      var parts = hostname.split('.');
      if (parts.length >= 2) {
        var name = parts.slice(0, -1).join('.');
        if (parts.length >= 3 && ['co', 'com', 'org', 'net'].indexOf(parts[parts.length - 2]) !== -1) {
          name = parts.slice(0, -2).join('.');
        }
        return name.charAt(0).toUpperCase() + name.slice(1);
      }
      return hostname;
    } catch (e) {
      return 'Unknown';
    }
  }

  function stripHtml(html) {
    if (!html) return '';
    var div = document.createElement('div');
    div.innerHTML = html;
    return (div.textContent || div.innerText || '').replace(/\s+/g, ' ').trim();
  }

  function cleanText(text) {
    if (!text) return '';
    return text.replace(/\s+/g, ' ').trim();
  }

  // === UI ===

  function createUI() {
    if (document.getElementById('gen-ui')) return;
    var el = document.createElement('div');
    el.id = 'gen-ui';
    el.innerHTML = '<span id="gen-dot"></span><span id="gen-text">Ready</span><span id="gen-count">0</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#4A90D9;color:#fff;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#gen-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#gen-count').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;';
    document.body.appendChild(el);
    el.onclick = function() { doExtract(true); };
  }

  function updateUI(text, count) {
    var t = document.getElementById('gen-text');
    var c = document.getElementById('gen-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }

  // === Extraction & Sending ===

  function doExtract(includeHeuristic) {
    if (sending) return;

    var jobs = [];

    // 1. Try JSON-LD first (best quality)
    var postings = getAllJsonLdJobPostings();
    for (var i = 0; i < postings.length; i++) {
      var job = extractJobFromJsonLd(postings[i]);
      if (job && job.Title && job.Url && !sentJobIds[job.Url]) {
        jobs.push(job);
      }
    }

    // 2. Try DOM job links (for search results pages)
    if (jobs.length === 0) {
      var cardInfos = findJobCards();
      for (var c = 0; c < cardInfos.length; c++) {
        var cardJob = extractJobFromCard(cardInfos[c]);
        if (cardJob && cardJob.Title && cardJob.Url && !sentJobIds[cardJob.Url]) {
          jobs.push(cardJob);
        }
      }
    }

    // 3. Heuristic fallback (manual trigger or single job detail pages)
    if (jobs.length === 0 && includeHeuristic) {
      var heuristicJob = extractHeuristic();
      if (heuristicJob && !sentJobIds[heuristicJob.Url]) {
        jobs.push(heuristicJob);
      }
    }

    if (jobs.length > 0) {
      console.log('[GEN] Extracted ' + jobs.length + ' job(s)');
      if (!uiCreated) {
        createUI();
        uiCreated = true;
      }
      sendJobs(jobs);
    }
  }

  function sendJobs(jobs) {
    if (sending || jobs.length === 0) return;
    sending = true;
    if (uiCreated) updateUI('Sending...', totalSent);

    var index = 0;
    function sendNext() {
      if (index >= jobs.length) {
        sending = false;
        if (uiCreated) updateUI('Done', totalSent);
        console.log('[GEN] Sent: ' + totalSent + ', Duplicates: ' + totalDuplicates);
        return;
      }

      var job = jobs[index];
      index++;

      apiFetch(SERVER_URL + '/api/jobs', {
        method: 'POST',
        headers: getHeaders(),
        body: JSON.stringify(job)
      })
      .then(function(resp) {
        var d = resp.data;
        sentJobIds[job.Url] = true;
        if (d && d.added === true) {
          totalSent++;
          console.log('[GEN] Added: ' + job.Title + ' (' + job.Company + ')');
        } else {
          totalDuplicates++;
          console.log('[GEN] Duplicate: ' + job.Title);
        }

        // Update description if we have one
        if (job.Description && job.Description.length > 50) {
          apiFetch(SERVER_URL + '/api/jobs/description', {
            method: 'PUT',
            headers: getHeaders(),
            body: JSON.stringify({
              Url: job.Url,
              Description: job.Description,
              Company: job.Company,
              Contacts: job.Contacts,
              JobType: job.JobType
            })
          }).catch(function() {});
        }

        if (uiCreated) updateUI('Sending...', totalSent);
        sendNext();
      })
      .catch(function(e) {
        console.log('[GEN] Error sending job:', e.message);
        sendNext();
      });
    }

    sendNext();
  }

  // === Console API ===

  window.GEN = {
    extract: function() {
      doExtract(true);
      return 'Extracting jobs (JSON-LD → DOM cards → heuristic)...';
    },
    status: function() {
      var postings = getAllJsonLdJobPostings();
      var cards = findJobCards();
      return {
        jsonLdJobs: postings.length,
        domCards: cards.length,
        sent: totalSent,
        duplicates: totalDuplicates,
        serverUrl: SERVER_URL,
        hasApiKey: !!API_KEY,
        url: getCurrentJobUrl(),
        source: getSourceFromHostname()
      };
    },
    debug: function() {
      var postings = getAllJsonLdJobPostings();
      var cardInfos = findJobCards();
      console.log('[GEN] === Debug Info ===');
      console.log('[GEN] URL:', getCurrentJobUrl());
      console.log('[GEN] Source:', getSourceFromHostname());
      console.log('[GEN] Schema.org JobPostings found:', postings.length);
      console.log('[GEN] Job links found on page:', cardInfos.length);
      if (postings.length > 0) {
        postings.forEach(function(p, i) {
          console.log('[GEN] JSON-LD Job ' + (i+1) + ':', p.title, '-', (p.hiringOrganization && p.hiringOrganization.name) || 'Unknown company');
        });
      }
      if (cardInfos.length > 0) {
        cardInfos.slice(0, 10).forEach(function(info, i) {
          var job = extractJobFromCard(info);
          console.log('[GEN] Link ' + (i+1) + ':', job.Title, '-', job.Company, '-', job.Location, '(' + job.Url + ')');
        });
        if (cardInfos.length > 10) console.log('[GEN] ... and ' + (cardInfos.length - 10) + ' more');
      }
      var ldScripts = document.querySelectorAll('script[type="application/ld+json"]');
      console.log('[GEN] Total LD+JSON scripts on page:', ldScripts.length);
      ldScripts.forEach(function(s, i) {
        try {
          var d = JSON.parse(s.textContent);
          console.log('[GEN] LD+JSON #' + (i+1) + ' @type:', d['@type'] || (Array.isArray(d) ? 'Array[' + d.length + ']' : 'unknown'));
        } catch(e) {
          console.log('[GEN] LD+JSON #' + (i+1) + ': parse error');
        }
      });
      return 'Debug info logged to console';
    },
    test: function() {
      console.log('[GEN] Testing connection to ' + SERVER_URL + '...');
      apiFetch(SERVER_URL + '/api/jobs/stats', { headers: getHeaders() })
        .then(function(resp) { console.log('[GEN] Connected! Stats:', resp.data); })
        .catch(function(e) { console.log('[GEN] Connection failed:', e.message); });
      return 'Testing connection...';
    },
    help: function() {
      console.log('[GEN] === Universal Job Extractor Commands ===');
      console.log('GEN.extract()  - Extract jobs (JSON-LD + DOM cards + heuristic)');
      console.log('GEN.status()   - Show extraction status');
      console.log('GEN.debug()    - Show debug info, JSON-LD and DOM card details');
      console.log('GEN.test()     - Test connection to server');
      console.log('GEN.help()     - Show this help');
      return 'Commands listed above';
    }
  };

})();
