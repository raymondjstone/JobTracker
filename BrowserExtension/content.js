(function() {
  var SERVER_URL = 'https://localhost:7046';
  var API_KEY = ''; // Will be loaded from storage
  var totalSent = 0;
  var totalDuplicates = 0;
  var sending = false;
  var sentJobIds = {}; // Track jobs already sent to server across extractions
  var fetchingDescriptions = false;
  
  // Auto-fetch settings
  var autoFetchEnabled = false;
  var autoFetchDelay = 3000; // milliseconds between navigations
  var autoFetchQueue = [];
  var autoFetchIndex = 0;

  console.log('[LJE] LinkedIn Job Extractor loaded');

  // Load server URL and API key from storage
  if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
      if (result.serverUrl) {
        SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        console.log('[LJE] Using server URL from settings:', SERVER_URL);
      }
      if (result.apiKey) {
        API_KEY = result.apiKey;
        console.log('[LJE] API key loaded');
      }
    });
  }

  // Helper function to get headers with API key
  function getHeaders() {
    var headers = { 
      'Content-Type': 'application/json',
      'Accept': 'application/json'
    };
    if (API_KEY) {
      headers['X-API-Key'] = API_KEY;
    }
    return headers;
  }

  // Mark that extension is installed (for detection from web pages)
  window.LinkedInJobExtractorInstalled = true;
  document.documentElement.setAttribute('data-lje-installed', 'true');

  createUI();
  setTimeout(doExtract, 2000);
  
  window.addEventListener('scroll', function() {
    setTimeout(doExtract, 1000);
  });
  
  setInterval(doExtract, 5000);
  
  // Check for jobs needing descriptions every 30 seconds
  setInterval(checkAndFetchDescriptions, 30000);
  // Initial check after 5 seconds
  setTimeout(checkAndFetchDescriptions, 5000);
  
  // Check if we're in auto-fetch mode on page load
  setTimeout(checkAutoFetchState, 1500);

  chrome.runtime.onMessage.addListener(function(message, sender, sendResponse) {
    if (message.action === 'extract') {
      console.log('[LJE] Extract triggered from popup');
      doExtract();
      sendResponse({ success: true, sent: totalSent });
    }
    return true;
  });

  function createUI() {
    if (document.getElementById('lje-ui')) return;
    var el = document.createElement('div');
    el.id = 'lje-ui';
    el.innerHTML = '<span id="lje-dot"></span><span id="lje-text">Ready</span><span id="lje-count">0</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#0a66c2;color:#fff;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#lje-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#lje-count').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;';
    document.body.appendChild(el);
    el.onclick = doExtract;
  }

  function updateUI(text, count) {
    var t = document.getElementById('lje-text');
    var c = document.getElementById('lje-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }
  
  function showAutoFetchUI() {
    var existing = document.getElementById('lje-autofetch-ui');
    if (existing) existing.remove();
    
    var el = document.createElement('div');
    el.id = 'lje-autofetch-ui';
    el.innerHTML = '<div id="lje-af-status">Auto-fetching descriptions...</div>' +
                   '<div id="lje-af-progress">Job <span id="lje-af-current">0</span> of <span id="lje-af-total">0</span></div>' +
                   '<div id="lje-af-job"></div>' +
                   '<button id="lje-af-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#0a66c2;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#lje-af-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#lje-af-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#lje-af-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#lje-af-stop').style.cssText = 'background:#dc3545;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#lje-af-stop').onclick = stopAutoFetch;
    document.body.appendChild(el);
  }
  
  function updateAutoFetchUI(current, total, jobTitle) {
    var c = document.getElementById('lje-af-current');
    var t = document.getElementById('lje-af-total');
    var j = document.getElementById('lje-af-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }
  
  function hideAutoFetchUI() {
    var el = document.getElementById('lje-autofetch-ui');
    if (el) el.remove();
  }

  function cleanText(text) {
    if (!text) return '';
    text = text.trim().replace(/\s+/g, ' ').replace(/\n/g, ' ');
    var len = text.length;
    if (len >= 6 && len % 2 === 0) {
      var half = len / 2;
      if (text.substring(0, half).toLowerCase() === text.substring(half).toLowerCase()) {
        return text.substring(0, half).trim();
      }
    }
    var words = text.split(' ');
    if (words.length >= 2 && words.length % 2 === 0) {
      var h = words.length / 2;
      var first = words.slice(0, h).join(' ');
      var second = words.slice(h).join(' ');
      if (first.toLowerCase() === second.toLowerCase()) {
        return first.trim();
      }
    }
    return text.trim();
  }

  function getPosterCompany() {
    // LinkedIn shows "Direct message the job poster from {Company}" on job detail pages
    try {
      var bodyText = document.body.innerText || '';
      var match = bodyText.match(/(?:Direct message the job poster from|job poster from)\s+(.+)/i);
      if (match) {
        var company = match[1].trim().split('\n')[0].trim();
        if (company.length > 0 && company.length < 200) {
          console.log('[LJE] Found poster company: ' + company);
          return company;
        }
      }
    } catch (e) { }
    return '';
  }

  function getCompanyFromPageTitle() {
    // LinkedIn page titles use "{Company} hiring {Title} in {Location} | LinkedIn"
    try {
      var title = document.title || '';
      var match = title.match(/^(.+?)\s+hiring\s+/i);
      if (match && match[1].length > 0 && match[1].length < 200) {
        console.log('[LJE] Found company from page title: ' + match[1]);
        return match[1].trim();
      }
    } catch (e) { }
    return '';
  }

  function getJobDescription() {
    // Method 1: Text markers — extract between "About the job" and end markers.
    // This works regardless of DOM structure.
    try {
      var bodyText = document.body.innerText || '';

      // Try multiple start markers
      var startMarkers = [/About the job\s*\n/i, /About this role\s*\n/i, /Job description\s*\n/i];
      var endMarkers = [/\nSet alert for similar jobs/i, /\nShow more$/im, /\nSee more$/im, /\nAbout the company/i, /\nSimilar jobs/i, /\nPeople also viewed/i, /\nActivity on this job/i];

      for (var si = 0; si < startMarkers.length; si++) {
        var aboutIdx = bodyText.search(startMarkers[si]);
        if (aboutIdx === -1) continue;
        var startOffset = aboutIdx + bodyText.slice(aboutIdx).match(startMarkers[si])[0].length;

        // Try each end marker, pick the closest one after start
        var bestEnd = -1;
        for (var ei = 0; ei < endMarkers.length; ei++) {
          var remaining = bodyText.substring(startOffset);
          var endMatch = remaining.search(endMarkers[ei]);
          if (endMatch !== -1) {
            var absEnd = startOffset + endMatch;
            if (bestEnd === -1 || absEnd < bestEnd) bestEnd = absEnd;
          }
        }

        var extracted = bestEnd !== -1
          ? bodyText.substring(startOffset, bestEnd).trim()
          : bodyText.substring(startOffset, startOffset + 10000).trim(); // no end marker: take up to 10k

        // Truncate at reasonable boundary if no end marker was found
        if (bestEnd === -1 && extracted.length > 3000) {
          extracted = extracted.substring(0, 3000);
        }

        if (extracted.length > 50) {
          console.log('[LJE] Found description via text marker (start #' + si + ', len=' + extracted.length + ')');
          return extracted.substring(0, 10000);
        }
      }
    } catch (e) {
      console.log('[LJE] Error in marker-based extraction: ' + e.message);
    }

    // Method 2: CSS selectors for known description containers
    var descSelectors = [
      // Current LinkedIn selectors (2025-2026)
      '#job-details',
      '.jobs-description__container',
      '.jobs-description-content',
      '.jobs-description__content',
      '.jobs-description-content__text',
      '.jobs-box__html-content',
      '.jobs-description',
      '.jobs-description-content__text--stretch',
      '.show-more-less-html__markup',
      // Collection/recommendation page selectors
      '.job-details-module__content',
      '.jobs-details__main-content',
      '.jobs-details-top-card__job-description',
      '.jobs-company__description',
      // Generic pattern matches
      '[class*="jobs-description"]',
      '[class*="job-details"]',
      '[class*="description-module"]',
      '[class*="job-detail"] [class*="description"]',
      '.job-view-layout [class*="description"]',
      'article[class*="jobs"]',
      // Aria-based selectors
      '[aria-label*="description" i]',
      '[aria-label*="job detail" i]'
    ];

    for (var i = 0; i < descSelectors.length; i++) {
      try {
        var els = document.querySelectorAll(descSelectors[i]);
        for (var ei2 = 0; ei2 < els.length; ei2++) {
          var el = els[ei2];
          if (el && el.innerText && el.innerText.trim().length > 50) {
            console.log('[LJE] Found description using selector: ' + descSelectors[i] + ' (len=' + el.innerText.trim().length + ')');
            return el.innerText.trim().substring(0, 10000);
          }
        }
      } catch (e) {
        // Invalid selector, skip
      }
    }

    // Method 3: Find the detail/right panel and look for substantial text blocks
    var detailPanelSelectors = [
      '.scaffold-layout__detail',
      '.jobs-search__job-details',
      '.job-details-module',
      '[class*="job-view"]',
      '[class*="detail-panel"]',
      '[class*="job-details"]',
      'main [class*="detail"]',
      'main'
    ];

    for (var pi = 0; pi < detailPanelSelectors.length; pi++) {
      try {
        var panel = document.querySelector(detailPanelSelectors[pi]);
        if (!panel) continue;

        var allDivs = panel.querySelectorAll('div, section, article');
        for (var j = 0; j < allDivs.length; j++) {
          var text = allDivs[j].innerText;
          if (text && text.trim().length > 200 && text.trim().length < 15000) {
            if (text.split('\n').length > 3) {
              console.log('[LJE] Found description in panel (' + detailPanelSelectors[pi] + ', len=' + text.trim().length + ')');
              return text.trim().substring(0, 10000);
            }
          }
        }
      } catch (e) { }
    }

    console.log('[LJE] WARNING: No description found on page. URL: ' + window.location.href.substring(0, 80));
    return '';
  }

  function getCurrentJobId() {
    // Try URL parameter first
    var urlParams = new URLSearchParams(window.location.search);
    var jobId = urlParams.get('currentJobId');
    if (jobId) return jobId;
    
    // Try URL path
    var match = window.location.href.match(/\/jobs\/view\/(\d+)/);
    if (match) return match[1];
    
    return '';
  }

  function getCurrentJobUrl() {
    var currentJobId = getCurrentJobId();
    return currentJobId ? 'https://www.linkedin.com/jobs/view/' + currentJobId : '';
  }

  function getCompanyFromDetailPage() {
    // Try multiple methods to get the company name from a job detail page
    var company = getPosterCompany();
    if (company) return company;

    // Try structured selectors
    var companySelectors = [
      '.job-details-jobs-unified-top-card__company-name a',
      '.job-details-jobs-unified-top-card__company-name',
      '[class*="company-name"] a',
      '[class*="company-name"]',
      '.jobs-unified-top-card__company-name a',
      '.jobs-unified-top-card__company-name',
      '.artdeco-entity-lockup__subtitle',
      '.topcard__org-name-link',
      '.topcard__flavor--black-link'
    ];
    for (var i = 0; i < companySelectors.length; i++) {
      try {
        var el = document.querySelector(companySelectors[i]);
        if (el) {
          var text = cleanText(el.textContent);
          if (text && text.length > 1 && text.length < 200) {
            return text;
          }
        }
      } catch (e) { }
    }

    return getCompanyFromPageTitle() || '';
  }

  function isJobUnavailable() {
    try {
      var bodyText = (document.body.innerText || '').toLowerCase();
      var indicators = [
        'no longer accepting applications',
        'this job is no longer available',
        'this job has been closed',
        'job has expired',
        'this position has been filled',
        'no longer available'
      ];
      for (var i = 0; i < indicators.length; i++) {
        if (bodyText.indexOf(indicators[i]) !== -1) {
          return indicators[i];
        }
      }
    } catch (e) { }
    return null;
  }

  function markJobUnavailable(url, reason) {
    if (!url) return Promise.resolve(false);

    return fetch(SERVER_URL + '/api/jobs/mark-unavailable', {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify({ Url: url, Reason: reason || 'Job no longer available' })
    })
    .then(function(r) { return r.json(); })
    .then(function(d) {
      if (d.success) {
        console.log('[LJE] Marked job as unavailable: ' + url.substring(0, 50));
      }
      return d.success;
    })
    .catch(function(e) {
      console.log('[LJE] Error marking unavailable: ' + e.message);
      return false;
    });
  }

  function updateDescription(url, description, company) {
    if (!url || !description || description.length < 50) return Promise.resolve(false);

    var body = { Url: url, Description: description };
    if (company) body.Company = company;

    return fetch(SERVER_URL + '/api/jobs/description', {
      method: 'PUT',
      headers: getHeaders(),
      body: JSON.stringify(body)
    })
    .then(function(r) {
      if (!r.ok) {
        return r.text().then(function(text) {
          console.log('[LJE] Error updating description - HTTP ' + r.status + ': ' + text.substring(0, 100));
          return { updated: false, error: text };
        });
      }
      return r.json();
    })
    .then(function(d) {
      if (d.updated) {
        console.log('[LJE] Description updated');
        return true;
      }
      return false;
    })
    .catch(function(e) {
      console.log('[LJE] Error updating description: ' + e.message);
      return false;
    });
  }

  function findAllJobCards() {
    var cards = [];
    
    // Method 1: Find job cards by data attributes
    var dataCards = document.querySelectorAll('[data-job-id], [data-occludable-job-id]');
    dataCards.forEach(function(card) {
      var jobId = card.getAttribute('data-job-id') || card.getAttribute('data-occludable-job-id');
      if (jobId) {
        cards.push({ element: card, jobId: jobId });
      }
    });
    
    // Method 2: Find by job card classes
    if (cards.length === 0) {
      var classCards = document.querySelectorAll('.job-card-container, .jobs-search-results__list-item, .scaffold-layout__list-item');
      classCards.forEach(function(card) {
        var link = card.querySelector('a[href*="/jobs/view/"], a[href*="currentJobId"]');
        if (link) {
          var jobId = '';
          var href = link.href;
          var match = href.match(/\/jobs\/view\/(\d+)/) || href.match(/currentJobId=(\d+)/);
          if (match) jobId = match[1];
          if (jobId) {
            cards.push({ element: card, jobId: jobId });
          }
        }
      });
    }
    
    // Method 3: Find any clickable job items in list
    if (cards.length === 0) {
      var listItems = document.querySelectorAll('li[class*="job"], div[class*="job-card"]');
      listItems.forEach(function(item) {
        var link = item.querySelector('a');
        if (link && link.href) {
          var match = link.href.match(/\/jobs\/view\/(\d+)/) || link.href.match(/currentJobId=(\d+)/);
          if (match) {
            cards.push({ element: item, jobId: match[1] });
          }
        }
      });
    }
    
    // Method 4: Just find ALL links that look like job links
    if (cards.length === 0) {
      var allLinks = document.querySelectorAll('a[href*="jobs"]');
      allLinks.forEach(function(link) {
        var match = link.href.match(/\/jobs\/view\/(\d+)/) || link.href.match(/currentJobId=(\d+)/);
        if (match) {
          cards.push({ element: link.closest('li') || link.closest('div') || link, jobId: match[1] });
        }
      });
    }

    console.log('[LJE] Found ' + cards.length + ' job cards using card detection');
    return cards;
  }

  function extractJobFromCard(card, jobId) {
    var el = card.element;
    var url = 'https://www.linkedin.com/jobs/view/' + jobId;
    
    // Find title
    var title = '';
    var titleSelectors = [
      '.job-card-list__title',
      '.job-card-container__link',
      '[class*="job-title"]',
      '[class*="title"]',
      'a[class*="job"]',
      'strong',
      'h3',
      'h4',
      'a'
    ];
    
    for (var i = 0; i < titleSelectors.length; i++) {
      var titleEl = el.querySelector(titleSelectors[i]);
      if (titleEl) {
        var text = cleanText(titleEl.textContent);
        if (text && text.length > 3 && text.length < 200) {
          title = text;
          break;
        }
      }
    }
    
    // If no title from selectors, try the element's own text
    if (!title) {
      var elText = cleanText(el.textContent);
      if (elText && elText.length > 3 && elText.length < 200) {
        // Take first line as title
        title = elText.split('\n')[0].trim();
        if (title.length > 100) {
          title = title.substring(0, 100);
        }
      }
    }
    
    if (!title || title.length < 3) {
      return null;
    }
    
    // Find company
    var company = '';
    var companySelectors = [
      '.job-card-container__company-name',
      '.job-card-container__primary-description',
      '[class*="company"]',
      '[class*="subtitle"]',
      'h4',
      '.artdeco-entity-lockup__subtitle'
    ];
    
    for (var j = 0; j < companySelectors.length; j++) {
      var compEl = el.querySelector(companySelectors[j]);
      if (compEl) {
        var compText = cleanText(compEl.textContent.split('\n')[0]);
        if (compText && compText.length > 1 && compText.length < 100 && compText !== title) {
          company = compText;
          break;
        }
      }
    }
    
    // Find location
    var location = '';
    var locSelectors = [
      '.job-card-container__metadata-item',
      '[class*="location"]',
      '[class*="metadata"]',
      '[class*="caption"]'
    ];
    
    for (var k = 0; k < locSelectors.length; k++) {
      var locEl = el.querySelector(locSelectors[k]);
      if (locEl) {
        var locText = locEl.textContent;
        if (locText.indexOf('Easy Apply') === -1 && locText.indexOf('applicant') === -1 && locText.indexOf('Promoted') === -1) {
          location = cleanText(locText);
          if (location && location.length > 3 && location !== company && location !== title) {
            break;
          }
        }
      }
    }
    
    return {
      Title: title,
      Company: company,
      Location: location,
      Description: '',
      JobType: 0,
      Salary: '',
      Url: url,
      DatePosted: new Date().toISOString(),
      IsRemote: location.toLowerCase().indexOf('remote') !== -1,
      Skills: [],
      Source: 'LinkedIn'
    };
  }

  function doExtract() {
    // Skip extraction if auto-fetch or crawl is running
    if (autoFetchEnabled) {
      console.log('[LJE] Skipping extraction - auto-fetch is running');
      return;
    }
    if (crawlEnabled) {
      console.log('[LJE] Skipping extraction - crawl is running');
      return;
    }
    if (inPageFetchRunning) {
      console.log('[LJE] Skipping extraction - in-page description fetch is running');
      return;
    }

    if (sending) return;
    updateUI('Scanning', totalSent);

    var jobs = [];
    var seenIds = {};

    // Find job cards
    var cards = findAllJobCards();
    
    cards.forEach(function(card) {
      if (seenIds[card.jobId]) return;
      seenIds[card.jobId] = true;
      
      var job = extractJobFromCard(card, card.jobId);
      if (job) {
        jobs.push(job);
      }
    });

    console.log('[LJE] Extracted ' + jobs.length + ' unique jobs');

    // Get description for currently viewed job
    var currentDesc = getJobDescription();
    var currentUrl = getCurrentJobUrl();
    var currentId = getCurrentJobId();
    console.log('[LJE] Current job: id=' + currentId + ', url=' + currentUrl + ', descLen=' + (currentDesc ? currentDesc.length : 0));

    if (currentDesc && currentDesc.length > 50 && currentUrl) {
      console.log('[LJE] Found description (' + currentDesc.length + ' chars) for job ' + currentId);
      
      var foundInList = false;
      for (var k = 0; k < jobs.length; k++) {
        if (jobs[k].Url === currentUrl) {
          jobs[k].Description = currentDesc;
          foundInList = true;
          break;
        }
      }
      
      if (!foundInList) {
        updateDescription(currentUrl, currentDesc, getCompanyFromDetailPage());
      }
    }

    // If viewing a job detail, also add it if not in list
    if (getCurrentJobId() && !seenIds[getCurrentJobId()]) {
      var h1 = document.querySelector('h1');
      if (h1) {
        var detailTitle = cleanText(h1.textContent);
        if (detailTitle && detailTitle.length > 3) {
          var compEl = document.querySelector('[class*="company-name"] a, [class*="company-name"]');
          var locEl = document.querySelector('[class*="bullet"], [class*="location"]');
          var detailCompany = getPosterCompany() || (compEl ? cleanText(compEl.textContent) : '') || getCompanyFromPageTitle();

          jobs.push({
            Title: detailTitle,
            Company: detailCompany,
            Location: locEl ? cleanText(locEl.textContent) : '',
            Description: currentDesc || '',
            JobType: 0,
            Salary: '',
            Url: currentUrl,
            DatePosted: new Date().toISOString(),
            IsRemote: false,
            Skills: [],
            Source: 'LinkedIn'
          });
          console.log('[LJE] Added detail view job: ' + detailTitle.substring(0, 30));
        }
      }
    }

    // Filter out jobs already sent to server
    var newJobs = jobs.filter(function(j) { return !sentJobIds[j.Url]; });
    console.log('[LJE] Total jobs: ' + jobs.length + ', new: ' + newJobs.length);

    if (newJobs.length > 0) {
      sendJobs(newJobs);
    } else {
      if (currentDesc && currentUrl) {
        updateDescription(currentUrl, currentDesc, getCompanyFromDetailPage());
      }
      updateUI('Monitoring', totalSent);
    }
  }

  // In-page description fetcher: clicks through sidebar job cards to load descriptions
  var inPageFetchRunning = false;

  function fetchDescriptionsInPage(jobsNeedingDesc) {
    if (inPageFetchRunning || autoFetchEnabled || crawlEnabled) return;

    // Build a map of job URLs that need descriptions
    var needsDesc = {};
    jobsNeedingDesc.forEach(function(j) { needsDesc[j.Url] = true; });

    // Find clickable job cards in the sidebar, deduplicated by jobId
    var cards = findAllJobCards();
    var cardsToClick = [];
    var seenJobIds = {};

    cards.forEach(function(card) {
      if (seenJobIds[card.jobId]) return; // skip duplicate DOM elements for same job
      seenJobIds[card.jobId] = true;

      var url = 'https://www.linkedin.com/jobs/view/' + card.jobId;
      if (needsDesc[url]) {
        var clickTarget = card.element.querySelector('a[href*="jobs"]') || card.element.querySelector('a') || card.element;
        cardsToClick.push({ url: url, element: clickTarget, jobId: card.jobId });
      }
    });

    if (cardsToClick.length === 0) {
      console.log('[LJE] No clickable cards found for description fetch');
      return;
    }

    console.log('[LJE] Will click through ' + cardsToClick.length + ' unique cards to fetch descriptions');
    inPageFetchRunning = true;
    var idx = 0;
    var descUpdated = 0;

    function clickNext() {
      if (idx >= cardsToClick.length || !inPageFetchRunning) {
        inPageFetchRunning = false;
        console.log('[LJE] In-page description fetch complete (' + descUpdated + '/' + cardsToClick.length + ' updated)');
        updateUI('Monitoring', totalSent);
        return;
      }

      var card = cardsToClick[idx];
      idx++;
      updateUI('Desc ' + idx + '/' + cardsToClick.length, totalSent);

      try {
        card.element.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      } catch(e) {
        try { card.element.click(); } catch(e2) {}
      }

      // Wait for the detail panel to load, then extract description
      setTimeout(function() {
        var desc = getJobDescription();
        if (desc && desc.length > 50) {
          descUpdated++;
          console.log('[LJE] Desc ' + idx + '/' + cardsToClick.length + ': ' + desc.length + ' chars for job ' + card.jobId);
          var company = getCompanyFromDetailPage();
          updateDescription(card.url, desc, company);
        } else {
          console.log('[LJE] Desc ' + idx + '/' + cardsToClick.length + ': none found for job ' + card.jobId);
        }
        setTimeout(clickNext, 1000);
      }, 2500);
    }

    clickNext();
  }

  function sendJobs(jobs) {
    sending = true;
    var i = 0;

    function next() {
      if (i >= jobs.length) {
        sending = false;
        updateUI('Done', totalSent);
        setTimeout(function() { updateUI('Monitoring', totalSent); }, 2000);
        // After sending, fetch descriptions for jobs that were sent without one
        var jobsNeedingDesc = jobs.filter(function(j) { return !j.Description || j.Description.length < 50; });
        if (jobsNeedingDesc.length > 0) {
          console.log('[LJE] ' + jobsNeedingDesc.length + ' jobs need descriptions - starting in-page fetch');
          setTimeout(function() { fetchDescriptionsInPage(jobsNeedingDesc); }, 2000);
        }
        return;
      }

      var job = jobs[i];
      i++;
      updateUI('Sending ' + i + '/' + jobs.length, totalSent);

      // Log what we're sending
      console.log('[LJE] Sending job:', {
        title: job.Title,
        company: job.Company,
        url: job.Url,
        hasApiKey: !!API_KEY
      });

      fetch(SERVER_URL + '/api/jobs', {
        method: 'POST',
        headers: getHeaders(),
        body: JSON.stringify(job)
      })
      .then(function(r) {
        console.log('[LJE] Response status:', r.status, r.statusText);
        if (!r.ok) {
          return r.text().then(function(text) {
            console.log('[LJE] Error response body:', text);
            throw new Error('HTTP ' + r.status + ': ' + text.substring(0, 200));
          });
        }
        return r.json();
      })
      .then(function(d) {
        sentJobIds[job.Url] = true;
        if (d.added === true) {
          totalSent++;
          console.log('[LJE] Added: ' + job.Title.substring(0, 40));
        } else {
          totalDuplicates++;
          console.log('[LJE] Duplicate or not added: ' + job.Title.substring(0, 40));
          if (job.Description && job.Description.length > 50) {
            updateDescription(job.Url, job.Description, job.Company);
          }
        }
        next();
      })
      .catch(function(e) {
        console.log('[LJE] Error: ' + e.message);
        next();
      });
    }

    next();
  }

  // Function to check server for jobs needing descriptions and fetch them
  function checkAndFetchDescriptions() {
    if (fetchingDescriptions || sending || inPageFetchRunning) return;

    // Only proceed if we're on a LinkedIn jobs page
    if (!window.location.href.includes('linkedin.com/jobs')) return;

    fetchingDescriptions = true;

    fetch(SERVER_URL + '/api/jobs/needing-descriptions?limit=50', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          fetchingDescriptions = false;
          return;
        }

        console.log('[LJE] Found ' + data.count + ' jobs needing descriptions');

        // Check if the currently viewed job is one that needs a description
        var currentJobId = getCurrentJobId();
        var currentUrl = getCurrentJobUrl();

        if (currentJobId) {
          var currentJobNeedsDesc = data.jobs.some(function(j) {
            return j.Url && j.Url.toLowerCase().includes(currentJobId);
          });

          if (currentJobNeedsDesc) {
            var desc = getJobDescription();
            if (desc && desc.length > 50) {
              console.log('[LJE] Current job needs description - sending it now');
              updateDescription(currentUrl, desc, getCompanyFromDetailPage());
            }
          }
        }

        // Check if any visible sidebar cards match jobs needing descriptions
        // and trigger in-page fetch if so
        if (!inPageFetchRunning && !autoFetchEnabled && !crawlEnabled) {
          var visibleCards = findAllJobCards();
          var jobsOnPage = [];
          var needsDescUrls = {};
          data.jobs.forEach(function(j) {
            var url = (j.Url || j.url || '').toLowerCase().replace(/\/$/, '');
            if (url) needsDescUrls[url] = true;
          });

          visibleCards.forEach(function(card) {
            var url = 'https://www.linkedin.com/jobs/view/' + card.jobId;
            var normalized = url.toLowerCase().replace(/\/$/, '');
            if (needsDescUrls[normalized]) {
              jobsOnPage.push({ Url: url });
            }
          });

          if (jobsOnPage.length > 0) {
            console.log('[LJE] ' + jobsOnPage.length + ' visible jobs need descriptions - starting in-page fetch');
            fetchingDescriptions = false;
            fetchDescriptionsInPage(jobsOnPage);
            return;
          }
        }

        fetchingDescriptions = false;
      })
      .catch(function(e) {
        console.log('[LJE] Error checking for missing descriptions: ' + e.message);
        fetchingDescriptions = false;
      });
  }
  
  // Auto-fetch functions
  function normalizeJobUrl(url) {
    if (!url) return '';
    return url.replace(/\/$/, '');
  }

  function startAutoFetch(delaySeconds) {
    if (autoFetchEnabled) {
      console.log('[LJE] Auto-fetch already running');
      return;
    }
    
    autoFetchDelay = (delaySeconds || 3) * 1000;
    console.log('[LJE] Starting auto-fetch with ' + (autoFetchDelay/1000) + 's delay');
    
    // Fetch list of ALL jobs needing descriptions (no limit)
    fetch(SERVER_URL + '/api/jobs/needing-descriptions', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        console.log('[LJE] API response: ' + data.count + ' jobs needing descriptions');
        
        if (!data.jobs || data.jobs.length === 0) {
          console.log('[LJE] No jobs need descriptions!');
          alert('All jobs already have descriptions!');
          return;
        }
        
        // Filter for LinkedIn jobs only (by Source property or URL)
        autoFetchQueue = data.jobs.filter(function(job) {
          var jobUrl = job.Url || job.url || '';
          var source = job.Source || job.source || '';
          return (source.toLowerCase() === 'linkedin' || jobUrl.includes('linkedin.com'));
        }).map(function(job) {
          return {
            Url: normalizeJobUrl(job.Url || job.url || ''),
            Title: job.Title || job.title || 'Unknown Job',
            Company: job.Company || job.company || ''
          };
        });
        
        console.log('[LJE] Filtered queue:', autoFetchQueue.length, 'jobs');
        if (autoFetchQueue.length > 0) {
          console.log('[LJE] First job in queue:', JSON.stringify(autoFetchQueue[0]));
        }
        
        if (autoFetchQueue.length === 0) {
          console.log('[LJE] No LinkedIn jobs need descriptions!');
          alert('No LinkedIn jobs need descriptions.');
          return;
        }
        
        autoFetchIndex = 0;
        autoFetchEnabled = true;
        
        // Store state in sessionStorage so it persists across page loads
        sessionStorage.setItem('lje-autofetch', JSON.stringify({
          enabled: true,
          delay: autoFetchDelay,
          queue: autoFetchQueue,
          index: autoFetchIndex
        }));
        
        console.log('[LJE] Auto-fetch queue: ' + autoFetchQueue.length + ' jobs');
        showAutoFetchUI();
        
        var firstJob = autoFetchQueue[0];
        updateAutoFetchUI(1, autoFetchQueue.length, firstJob.Title);
        
        // Navigate to first job
        navigateToNextJob();
      })
      .catch(function(e) {
        console.log('[LJE] Error starting auto-fetch: ' + e.message);
        alert('Error starting auto-fetch: ' + e.message);
      });
  }
  
  function stopAutoFetch() {
    autoFetchEnabled = false;
    autoFetchQueue = [];
    autoFetchIndex = 0;
    sessionStorage.removeItem('lje-autofetch');
    hideAutoFetchUI();
    console.log('[LJE] Auto-fetch stopped');
  }
  
  function checkAutoFetchState() {
    var state = sessionStorage.getItem('lje-autofetch');
    if (!state) return;
    
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled && parsed.queue && parsed.queue.length > 0) {
        autoFetchEnabled = true;
        autoFetchDelay = parsed.delay || 3000;
        autoFetchQueue = parsed.queue;
        autoFetchIndex = parsed.index || 0;
        
        console.log('[LJE] Resuming auto-fetch at job ' + (autoFetchIndex + 1) + ' of ' + autoFetchQueue.length);
        showAutoFetchUI();
        
        // Process current page
        processCurrentPageForAutoFetch();
      }
    } catch (e) {
      console.log('[LJE] Error restoring auto-fetch state: ' + e.message);
      sessionStorage.removeItem('lje-autofetch');
    }
  }
  
  function processCurrentPageForAutoFetch() {
    if (!autoFetchEnabled || autoFetchIndex >= autoFetchQueue.length) {
      completeAutoFetch();
      return;
    }
    
    var currentJob = autoFetchQueue[autoFetchIndex];
    var jobTitle = (currentJob && currentJob.Title) ? currentJob.Title : 'Unknown Job';
    updateAutoFetchUI(autoFetchIndex + 1, autoFetchQueue.length, jobTitle);
    
    // Wait for page to load, then get description
    setTimeout(function() {
      var desc = getJobDescription();
      // Use the queue's normalized URL for update
      var queueUrl = currentJob && currentJob.Url ? normalizeJobUrl(currentJob.Url) : '';
      console.log('[LJE] Auto-fetch: About to update description', {
        queueUrl: queueUrl,
        descLength: desc ? desc.length : 0,
        descPreview: desc ? desc.substring(0, 80) : '',
        jobTitle: jobTitle
      });
      
      if (desc && desc.length > 50) {
        var company = getCompanyFromDetailPage();
        updateDescription(queueUrl, desc, company).then(function(result) {
          console.log('[LJE] Auto-fetch: updateDescription result:', result);
          // Move to next job
          autoFetchIndex++;
          
          // Update sessionStorage
          var state = JSON.parse(sessionStorage.getItem('lje-autofetch') || '{}');
          state.index = autoFetchIndex;
          sessionStorage.setItem('lje-autofetch', JSON.stringify(state));
          
          if (autoFetchIndex >= autoFetchQueue.length) {
            completeAutoFetch();
          } else {
            // Wait configured delay then navigate to next
            setTimeout(navigateToNextJob, autoFetchDelay);
          }
        });
      } else {
        // Check if job is no longer available
        var unavailableReason = isJobUnavailable();
        if (unavailableReason) {
          console.log('[LJE] Auto-fetch: Job unavailable - ' + unavailableReason);
          markJobUnavailable(queueUrl, unavailableReason).then(function() {
            autoFetchIndex++;
            var state = JSON.parse(sessionStorage.getItem('lje-autofetch') || '{}');
            state.index = autoFetchIndex;
            sessionStorage.setItem('lje-autofetch', JSON.stringify(state));
            if (autoFetchIndex >= autoFetchQueue.length) {
              completeAutoFetch();
            } else {
              setTimeout(navigateToNextJob, autoFetchDelay);
            }
          });
        } else {
          console.log('[LJE] Auto-fetch: No description found, skipping...');
          autoFetchIndex++;
          var state = JSON.parse(sessionStorage.getItem('lje-autofetch') || '{}');
          state.index = autoFetchIndex;
          sessionStorage.setItem('lje-autofetch', JSON.stringify(state));
          if (autoFetchIndex >= autoFetchQueue.length) {
            completeAutoFetch();
          } else {
            setTimeout(navigateToNextJob, 1000); // Shorter delay for skipped jobs
          }
        }
      }
    }, 2000); // Wait 2s for page content to load
  }
  
  function navigateToNextJob() {
    if (!autoFetchEnabled || autoFetchIndex >= autoFetchQueue.length) {
      completeAutoFetch();
      return;
    }
    
    var job = autoFetchQueue[autoFetchIndex];
    var jobTitle = (job && job.Title) ? job.Title : 'Unknown Job';
    console.log('[LJE] Auto-fetch: Navigating to job ' + (autoFetchIndex + 1) + ': ' + jobTitle.substring(0, Math.min(30, jobTitle.length)));
    updateAutoFetchUI(autoFetchIndex + 1, autoFetchQueue.length, jobTitle);
    
    // Navigate to the job URL
    if (job && job.Url) {
      window.location.href = job.Url;
    } else {
      console.log('[LJE] Auto-fetch: Invalid job URL, skipping...');
      autoFetchIndex++;
      setTimeout(navigateToNextJob, 500);
    }
  }
  
  function completeAutoFetch() {
    var totalProcessed = autoFetchIndex;
    stopAutoFetch();
    console.log('[LJE] Auto-fetch complete! Processed ' + totalProcessed + ' jobs');
    alert('Auto-fetch complete!\nProcessed ' + totalProcessed + ' jobs.');
  }

  // === Crawl Functions (paginate through search results) ===
  var crawlEnabled = false;
  var crawlDelay = 5000;
  var crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
  var crawlMaxPages = 20;

  function startCrawl(delaySeconds, maxPages) {
    if (crawlEnabled || autoFetchEnabled) {
      console.log('[LJE] Crawl or auto-fetch already running');
      return;
    }
    crawlDelay = (delaySeconds || 5) * 1000;
    crawlMaxPages = maxPages || 20;
    crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
    crawlEnabled = true;

    sessionStorage.setItem('lje-crawl', JSON.stringify({
      enabled: true,
      delay: crawlDelay,
      maxPages: crawlMaxPages,
      results: crawlResults
    }));

    console.log('[LJE] Starting crawl with ' + (crawlDelay/1000) + 's delay, max ' + crawlMaxPages + ' pages');
    showCrawlUI();
    processCrawlPage();
  }

  function stopCrawl() {
    crawlEnabled = false;
    sessionStorage.removeItem('lje-crawl');
    hideCrawlUI();
    console.log('[LJE] Crawl stopped. Pages: ' + crawlResults.pagesScanned + ', Found: ' + crawlResults.jobsFound + ', Added: ' + crawlResults.jobsAdded);
  }

  function checkCrawlState() {
    var state = sessionStorage.getItem('lje-crawl');
    if (!state) return;
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled) {
        crawlEnabled = true;
        crawlDelay = parsed.delay || 5000;
        crawlMaxPages = parsed.maxPages || 20;
        crawlResults = parsed.results || { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
        console.log('[LJE] Resuming crawl - page ' + (crawlResults.pagesScanned + 1));
        showCrawlUI();
        processCrawlPage();
      }
    } catch (e) {
      sessionStorage.removeItem('lje-crawl');
    }
  }

  function processCrawlPage() {
    if (!crawlEnabled) return;
    if (crawlResults.pagesScanned >= crawlMaxPages) {
      completeCrawl();
      return;
    }

    updateCrawlUI();

    // Wait for page to load then extract
    setTimeout(function() {
      if (!crawlEnabled) return;

      var cards = findAllJobCards();
      var pageJobs = [];
      var seenIds = {};

      cards.forEach(function(card) {
        if (seenIds[card.jobId]) return;
        seenIds[card.jobId] = true;
        var job = extractJobFromCard(card, card.jobId);
        if (job) pageJobs.push(job);
      });

      console.log('[LJE] Crawl page ' + (crawlResults.pagesScanned + 1) + ': found ' + pageJobs.length + ' jobs');
      crawlResults.jobsFound += pageJobs.length;
      crawlResults.pagesScanned++;

      // Send jobs then navigate to next page
      crawlSendJobs(pageJobs, function(added) {
        crawlResults.jobsAdded += added;
        saveCrawlState();
        updateCrawlUI();

        if (crawlResults.pagesScanned >= crawlMaxPages) {
          completeCrawl();
          return;
        }

        // Find next page
        var nextUrl = findNextPageUrl();
        if (nextUrl) {
          console.log('[LJE] Crawl: navigating to next page in ' + (crawlDelay/1000) + 's');
          setTimeout(function() {
            if (crawlEnabled) window.location.href = nextUrl;
          }, crawlDelay);
        } else {
          console.log('[LJE] Crawl: no next page found');
          completeCrawl();
        }
      });
    }, 3000);
  }

  function findNextPageUrl() {
    // LinkedIn pagination - try multiple selectors
    var nextBtn = document.querySelector('button[aria-label="View next page"]') ||
                  document.querySelector('.jobs-search-pagination__indicator-button--next') ||
                  document.querySelector('li.active + li .jobs-search-pagination__indicator-button');

    if (nextBtn) {
      // If it's a button in a paginated list, click it and get URL
      // Instead, construct URL from current page number
    }

    // URL-based pagination
    var url = new URL(window.location.href);
    var start = parseInt(url.searchParams.get('start') || '0');
    url.searchParams.set('start', start + 25);
    return url.toString();
  }

  function crawlSendJobs(jobs, callback) {
    var added = 0;
    var i = 0;

    function next() {
      if (i >= jobs.length) {
        callback(added);
        return;
      }
      var job = jobs[i];
      i++;

      if (sentJobIds[job.Url]) {
        next();
        return;
      }

      fetch(SERVER_URL + '/api/jobs', {
        method: 'POST',
        headers: getHeaders(),
        body: JSON.stringify(job)
      })
      .then(function(r) { return r.ok ? r.json() : { added: false }; })
      .then(function(d) {
        sentJobIds[job.Url] = true;
        if (d.added === true) {
          added++;
          totalSent++;
        }
        next();
      })
      .catch(function() { next(); });
    }
    next();
  }

  function saveCrawlState() {
    sessionStorage.setItem('lje-crawl', JSON.stringify({
      enabled: true,
      delay: crawlDelay,
      maxPages: crawlMaxPages,
      results: crawlResults
    }));
  }

  function completeCrawl() {
    var results = crawlResults;
    stopCrawl();
    var msg = 'Crawl complete!\nPages: ' + results.pagesScanned + '\nJobs found: ' + results.jobsFound + '\nNew jobs added: ' + results.jobsAdded;
    console.log('[LJE] ' + msg);
    alert(msg);
  }

  function showCrawlUI() {
    var existing = document.getElementById('lje-crawl-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'lje-crawl-ui';
    el.innerHTML = '<div id="lje-cr-status">Crawling search results...</div>' +
                   '<div id="lje-cr-progress">Page <span id="lje-cr-page">0</span> | Found <span id="lje-cr-found">0</span> | Added <span id="lje-cr-added">0</span></div>' +
                   '<button id="lje-cr-stop">Stop Crawl</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#057642;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#lje-cr-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#lje-cr-progress').style.cssText = 'margin-bottom:12px;';
    el.querySelector('#lje-cr-stop').style.cssText = 'background:#dc3545;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#lje-cr-stop').onclick = stopCrawl;
    document.body.appendChild(el);
  }

  function updateCrawlUI() {
    var p = document.getElementById('lje-cr-page');
    var f = document.getElementById('lje-cr-found');
    var a = document.getElementById('lje-cr-added');
    if (p) p.textContent = crawlResults.pagesScanned;
    if (f) f.textContent = crawlResults.jobsFound;
    if (a) a.textContent = crawlResults.jobsAdded;
  }

  function hideCrawlUI() {
    var el = document.getElementById('lje-crawl-ui');
    if (el) el.remove();
  }

  // Check crawl state on page load
  setTimeout(checkCrawlState, 2000);

  window.LJE = {
    extract: doExtract,
    status: function() { return { sent: totalSent, dup: totalDuplicates }; },
    test: function() {
      fetch(SERVER_URL + '/api/jobs', {
        headers: getHeaders()
      })
        .then(function(r) { return r.json(); })
        .then(function(d) { console.log('[LJE] Server has ' + d.length + ' jobs'); })
        .catch(function(e) { console.log('[LJE] Error: ' + e.message); });
    },
    sendTest: function() {
      var job = { Title: 'Test ' + Date.now(), Company: 'Test Co', Location: 'Remote', Description: '', JobType: 0, Salary: '', Url: 'https://linkedin.com/jobs/view/test' + Date.now(), DatePosted: new Date().toISOString(), IsRemote: true, Skills: [] };
      fetch(SERVER_URL + '/api/jobs', { method: 'POST', headers: getHeaders(), body: JSON.stringify(job) })
        .then(function(r) {
          if (!r.ok) {
            return r.text().then(function(text) {
              console.log('[LJE] Error response:', text);
              throw new Error('HTTP ' + r.status);
            });
          }
          return r.json();
        })
        .then(function(d) { console.log('[LJE] Result:', d); })
        .catch(function(e) { console.log('[LJE] Error:', e.message); });
    },
    getDesc: function() {
      var desc = getJobDescription();
      console.log('[LJE] Current Job ID:', getCurrentJobId());
      console.log('[LJE] Description (' + desc.length + ' chars):', desc.substring(0, 300));
      return desc;
    },
    updateDesc: function() {
      var desc = getJobDescription();
      var jobId = getCurrentJobId();
      if (desc && jobId) {
        var url = 'https://www.linkedin.com/jobs/view/' + jobId;
        updateDescription(url, desc, getCompanyFromDetailPage());
        console.log('[LJE] Sent description update for job ' + jobId);
      } else {
        console.log('[LJE] No description or job ID found');
      }
    },
    fetchMissing: checkAndFetchDescriptions,
    
    // Auto-fetch functions
    autoFetch: function(delaySeconds) {
      startAutoFetch(delaySeconds || 3);
    },
    stopAutoFetch: stopAutoFetch,
    autoFetchStatus: function() {
      return {
        enabled: autoFetchEnabled,
        delay: autoFetchDelay / 1000,
        total: autoFetchQueue.length,
        current: autoFetchIndex,
        remaining: autoFetchQueue.length - autoFetchIndex
      };
    },
    
    crawl: function(delaySeconds, maxPages) {
      startCrawl(delaySeconds || 5, maxPages || 20);
    },
    stopCrawl: stopCrawl,
    crawlStatus: function() {
      return {
        enabled: crawlEnabled,
        delay: crawlDelay / 1000,
        pagesScanned: crawlResults.pagesScanned,
        jobsFound: crawlResults.jobsFound,
        jobsAdded: crawlResults.jobsAdded
      };
    },

    debug: function() {
      console.log('[LJE] === DEBUG ===');
      console.log('[LJE] URL:', window.location.href);
      console.log('[LJE] Server URL:', SERVER_URL);
      console.log('[LJE] API Key:', API_KEY ? '(set)' : '(not set)');
      console.log('[LJE] Current Job ID:', getCurrentJobId());
      
      // Show what selectors find
      console.log('[LJE] Checking selectors:');
      console.log('  data-job-id elements:', document.querySelectorAll('[data-job-id]').length);
      console.log('  data-occludable-job-id:', document.querySelectorAll('[data-occludable-job-id]').length);
      console.log('  .job-card-container:', document.querySelectorAll('.job-card-container').length);
      console.log('  .scaffold-layout__list-item:', document.querySelectorAll('.scaffold-layout__list-item').length);
      console.log('  a[href*="jobs/view"]:', document.querySelectorAll('a[href*="jobs/view"]').length);
      console.log('  a[href*="currentJobId"]:', document.querySelectorAll('a[href*="currentJobId"]').length);
      console.log('  li[class*="job"]:', document.querySelectorAll('li[class*="job"]').length);
      
      var cards = findAllJobCards();
      console.log('[LJE] Total cards found:', cards.length);
      
      if (cards.length > 0) {
        console.log('[LJE] First 3 jobs:');
        for (var i = 0; i < Math.min(3, cards.length); i++) {
          var job = extractJobFromCard(cards[i], cards[i].jobId);
          console.log('  ' + (i+1) + '. ID:' + cards[i].jobId + ' - "' + (job ? job.Title.substring(0, 40) : 'NO TITLE') + '"');
        }
      }
      
      console.log('[LJE] Description length:', getJobDescription().length);
      console.log('[LJE] Auto-fetch status:', window.LJE.autoFetchStatus());
      
      // Check for jobs needing descriptions
      fetch(SERVER_URL + '/api/jobs/needing-descriptions?limit=5', {
        headers: getHeaders()
      })
        .then(function(r) { return r.json(); })
        .then(function(d) { 
          console.log('[LJE] Raw API response:', JSON.stringify(d));
          console.log('[LJE] Jobs needing descriptions:', d.count);
          if (d.jobs && d.jobs.length > 0) {
            d.jobs.forEach(function(j, i) {
              var title = j.Title || j.title || 'NO TITLE';
              var company = j.Company || j.company || 'NO COMPANY';
              var url = j.Url || j.url || 'NO URL';
              console.log('  ' + (i+1) + '. ' + title.substring(0, 40) + ' - ' + company + ' - ' + url.substring(0, 50));
            });
          }
        })
        .catch(function(e) { console.log('[LJE] Error checking: ' + e.message); });
    },
    clear: function() {
      if (confirm('Delete ALL jobs from database?')) {
        fetch(SERVER_URL + '/api/jobs/clear', { 
          method: 'DELETE',
          headers: getHeaders()
        })
          .then(function(r) { return r.json(); })
          .then(function(d) { 
            console.log('[LJE] Cleared:', d);
            totalSent = 0;
            totalDuplicates = 0;
            updateUI('Cleared', 0);
          });
      }
    },
    help: function() {
      console.log('[LJE] === COMMANDS ===');
      console.log('LJE.extract()         - Extract jobs from current page');
      console.log('LJE.debug()           - Show debug info');
      console.log('LJE.test()            - Test server connection');
      console.log('LJE.status()          - Show send statistics');
      console.log('LJE.getDesc()         - Get current job description');
      console.log('LJE.updateDesc()      - Send current description to server');
      console.log('LJE.fetchMissing()    - Check for jobs needing descriptions');
      console.log('');
      console.log('=== AUTO-FETCH ===');
      console.log('LJE.autoFetch(3)      - Start auto-fetching descriptions (3s delay)');
      console.log('LJE.autoFetch(5)      - Start with 5 second delay');
      console.log('LJE.stopAutoFetch()   - Stop auto-fetching');
      console.log('LJE.autoFetchStatus() - Check auto-fetch progress');
      console.log('');
      console.log('');
      console.log('=== CRAWL ===');
      console.log('LJE.crawl(5, 20)      - Crawl search results (5s delay, 20 pages max)');
      console.log('LJE.stopCrawl()       - Stop crawling');
      console.log('LJE.crawlStatus()     - Check crawl progress');
      console.log('');
      console.log('LJE.clear()           - Delete all jobs (with confirmation)');
    }
  };

  console.log('[LJE] Ready! Type LJE.help() for commands. LJE.autoFetch(3) to auto-fetch descriptions.');
})();
