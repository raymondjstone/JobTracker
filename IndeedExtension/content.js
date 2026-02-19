(function() {
  var SERVER_URL = 'https://localhost:7046';
  var API_KEY = ''; // Will be loaded from storage
  var totalSent = 0;
  var totalDuplicates = 0;
  var sending = false;
  var fetchingDescriptions = false;
  var sentJobIds = {}; // Track jobs already sent to server across extractions

  // Auto-fetch settings
  var autoFetchEnabled = false;
  var autoFetchDelay = 3000;
  var autoFetchQueue = [];
  var autoFetchIndex = 0;

  console.log('[IND] Indeed Job Extractor loaded');

  // Load server URL and API key from storage
  if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
      if (result.serverUrl) {
        SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        console.log('[IND] Using server URL from settings:', SERVER_URL);
      }
      if (result.apiKey) {
        API_KEY = result.apiKey;
        console.log('[IND] API key loaded');
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

  // Mark that extension is installed
  window.IndeedExtractorInstalled = true;
  document.documentElement.setAttribute('data-ind-installed', 'true');

  createUI();
  setTimeout(doExtract, 2000);

  window.addEventListener('scroll', function() {
    setTimeout(doExtract, 1000);
  });

  setInterval(doExtract, 5000);
  setInterval(checkAndFetchDescriptions, 30000);
  setTimeout(checkAndFetchDescriptions, 5000);
  setTimeout(checkAutoFetchState, 1500);
  setTimeout(checkAvailCheckState, 1500);

  function createUI() {
    if (document.getElementById('ind-ui')) return;
    var el = document.createElement('div');
    el.id = 'ind-ui';
    el.innerHTML = '<span id="ind-dot"></span><span id="ind-text">Ready</span><span id="ind-count">0</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#2557a7;color:#fff;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#ind-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#ind-count').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;';
    document.body.appendChild(el);
    el.onclick = doExtract;
  }

  function updateUI(text, count) {
    var t = document.getElementById('ind-text');
    var c = document.getElementById('ind-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }

  function showAutoFetchUI() {
    var existing = document.getElementById('ind-autofetch-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'ind-autofetch-ui';
    el.innerHTML = '<div id="ind-af-status">Auto-fetching descriptions...</div>' +
                   '<div id="ind-af-progress">Job <span id="ind-af-current">0</span> of <span id="ind-af-total">0</span></div>' +
                   '<div id="ind-af-job"></div>' +
                   '<button id="ind-af-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#2557a7;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#ind-af-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#ind-af-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#ind-af-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#ind-af-stop').style.cssText = 'background:#1a3d6e;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#ind-af-stop').onclick = stopAutoFetch;
    document.body.appendChild(el);
  }

  function updateAutoFetchUI(current, total, jobTitle) {
    var c = document.getElementById('ind-af-current');
    var t = document.getElementById('ind-af-total');
    var j = document.getElementById('ind-af-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }

  function hideAutoFetchUI() {
    var el = document.getElementById('ind-autofetch-ui');
    if (el) el.remove();
  }

  function cleanText(text) {
    if (!text) return '';
    text = text.trim().replace(/\s+/g, ' ').replace(/\n/g, ' ');
    return text.trim();
  }

  function getJobDescription() {
    // Indeed description selectors
    var descSelectors = [
      '#jobDescriptionText',
      '.jobsearch-jobDescriptionText',
      '.jobsearch-JobComponent-description',
      '[class*="jobDescription"]',
      '[class*="job-description"]',
      '.job-description',
      '#job-description',
      '[data-testid="jobDescriptionText"]',
      '.jobsearch-ViewJobLayout-jobDisplay',
      '[class*="ViewJob"] [class*="description"]'
    ];

    for (var i = 0; i < descSelectors.length; i++) {
      try {
        var el = document.querySelector(descSelectors[i]);
        if (el && el.innerText && el.innerText.trim().length > 50) {
          console.log('[IND] Found description using selector: ' + descSelectors[i]);
          return el.innerText.trim().substring(0, 10000);
        }
      } catch (e) {
        // Invalid selector, skip
      }
    }

    // Fallback: find substantial text in main content
    var mainContent = document.querySelector('main') ||
                      document.querySelector('[role="main"]') ||
                      document.querySelector('.jobsearch-ViewJobLayout');
    if (mainContent) {
      var allDivs = mainContent.querySelectorAll('div, section, article');
      for (var j = 0; j < allDivs.length; j++) {
        var text = allDivs[j].innerText;
        if (text && text.trim().length > 200 && text.trim().length < 15000) {
          if (text.split('\n').length > 3) {
            console.log('[IND] Found description using fallback method');
            return text.trim().substring(0, 10000);
          }
        }
      }
    }

    return '';
  }

  function getCurrentJobId() {
    // Try to extract job ID from URL
    var patterns = [
      /[?&]vjk=([a-f0-9]+)/i,
      /[?&]jk=([a-f0-9]+)/i,
      /\/viewjob\?.*jk=([a-f0-9]+)/i,
      /\/rc\/clk\?jk=([a-f0-9]+)/i,
      /[?&]fccid=([a-f0-9]+)/i
    ];

    for (var i = 0; i < patterns.length; i++) {
      var match = window.location.href.match(patterns[i]);
      if (match) return match[1];
    }

    // Try data attribute on page
    var jobEl = document.querySelector('[data-jk]');
    if (jobEl) return jobEl.getAttribute('data-jk');

    return '';
  }

  function getCurrentJobUrl() {
    var jobId = getCurrentJobId();
    if (jobId) {
      // Construct a clean Indeed job URL
      var baseUrl = window.location.origin;
      return baseUrl + '/viewjob?jk=' + jobId;
    }
    return window.location.href.split('&')[0];
  }

  function normalizeJobUrl(url) {
    if (!url) return '';
    // Extract job key and create normalized URL
    var match = url.match(/jk=([a-f0-9]+)/i);
    if (match) {
      return 'https://www.indeed.com/viewjob?jk=' + match[1];
    }
    return url.replace(/\/$/, '').split('?')[0];
  }

  function updateDescription(url, description) {
    if (!url || !description || description.length < 50) return Promise.resolve(false);

    return fetch(SERVER_URL + '/api/jobs/description', {
      method: 'PUT',
      headers: getHeaders(),
      body: JSON.stringify({ Url: url, Description: description })
    })
    .then(function(r) {
      if (!r.ok) {
        return r.text().then(function(text) {
          console.log('[IND] Error updating description - HTTP ' + r.status + ': ' + text.substring(0, 100));
          return { updated: false };
        });
      }
      return r.json();
    })
    .then(function(d) {
      if (d.updated) {
        console.log('[IND] Description updated');
        return true;
      }
      return false;
    })
    .catch(function(e) {
      console.log('[IND] Error updating description: ' + e.message);
      return false;
    });
  }

  function findAllJobCards() {
    var cards = [];

    // Indeed job card selectors
    var cardSelectors = [
      '.job_seen_beacon',
      '.jobsearch-ResultsList > li',
      '.tapItem',
      '[data-jk]',
      '.result',
      '.jobsearch-SerpJobCard',
      '[class*="jobCard"]',
      '[class*="JobCard"]',
      'article[class*="job"]',
      '.css-1m4cuuf' // Indeed's dynamic class pattern
    ];

    for (var s = 0; s < cardSelectors.length; s++) {
      try {
        var foundCards = document.querySelectorAll(cardSelectors[s]);
        if (foundCards.length > 0) {
          console.log('[IND] Found ' + foundCards.length + ' cards using selector: ' + cardSelectors[s]);
          foundCards.forEach(function(card) {
            var jobId = card.getAttribute('data-jk') ||
                       card.querySelector('[data-jk]')?.getAttribute('data-jk');

            if (!jobId) {
              // Try to find job ID from link
              var link = card.querySelector('a[href*="jk="], a[data-jk]');
              if (link) {
                jobId = link.getAttribute('data-jk');
                if (!jobId) {
                  var match = link.href.match(/jk=([a-f0-9]+)/i);
                  if (match) jobId = match[1];
                }
              }
            }

            if (jobId) {
              cards.push({
                element: card,
                jobId: jobId,
                url: 'https://www.indeed.com/viewjob?jk=' + jobId
              });
            }
          });
          if (cards.length > 0) break;
        }
      } catch (e) {
        // Invalid selector
      }
    }

    // Deduplicate by jobId
    var seen = {};
    cards = cards.filter(function(card) {
      if (seen[card.jobId]) return false;
      seen[card.jobId] = true;
      return true;
    });

    console.log('[IND] Found ' + cards.length + ' unique job cards');
    return cards;
  }

  function extractJobFromCard(card) {
    var el = card.element;
    var url = card.url;

    // Find title
    var title = '';
    var titleSelectors = [
      '.jobTitle',
      '[class*="jobTitle"]',
      'h2.jobTitle',
      'h2 a',
      '[data-testid="jobTitle"]',
      '.title',
      'a[data-jk]',
      'h2',
      'a'
    ];

    for (var i = 0; i < titleSelectors.length; i++) {
      var titleEl = el.querySelector(titleSelectors[i]);
      if (titleEl) {
        var text = cleanText(titleEl.textContent);
        // Filter out "new" badges and other noise
        text = text.replace(/^new\s*/i, '').trim();
        if (text && text.length > 3 && text.length < 200) {
          title = text;
          break;
        }
      }
    }

    if (!title || title.length < 3) {
      return null;
    }

    // Find company
    var company = '';
    var companySelectors = [
      '.companyName',
      '[class*="companyName"]',
      '[data-testid="company-name"]',
      '.company',
      '[class*="company"]',
      '.companyInfo'
    ];

    for (var j = 0; j < companySelectors.length; j++) {
      var compEl = el.querySelector(companySelectors[j]);
      if (compEl) {
        var compText = cleanText(compEl.textContent);
        if (compText && compText.length > 1 && compText.length < 100 && compText !== title) {
          company = compText;
          break;
        }
      }
    }

    // Find location
    var location = '';
    var locSelectors = [
      '.companyLocation',
      '[class*="companyLocation"]',
      '[data-testid="text-location"]',
      '.location',
      '[class*="location"]'
    ];

    for (var k = 0; k < locSelectors.length; k++) {
      var locEl = el.querySelector(locSelectors[k]);
      if (locEl) {
        var locText = cleanText(locEl.textContent);
        if (locText && locText.length > 2 && locText !== company && locText !== title) {
          location = locText;
          break;
        }
      }
    }

    // Find salary
    var salary = '';
    var salarySelectors = [
      '.salary-snippet',
      '[class*="salary"]',
      '.salaryText',
      '[class*="Salary"]',
      '.metadata.salary-snippet-container'
    ];

    for (var m = 0; m < salarySelectors.length; m++) {
      var salEl = el.querySelector(salarySelectors[m]);
      if (salEl) {
        var salText = cleanText(salEl.textContent);
        if (salText && salText.length > 1) {
          salary = salText;
          break;
        }
      }
    }

    // Check for remote status in multiple places
    var isRemote = checkIsRemote(el, location);

    return {
      Title: title,
      Company: company,
      Location: location,
      Description: '',
      JobType: 0,
      Salary: salary,
      Url: url,
      DatePosted: new Date().toISOString(),
      IsRemote: isRemote,
      Skills: [],
      Source: 'Indeed'
    };
  }

  function checkIsRemote(cardEl, locationText) {
    // Remote-related keywords to search for
    var remoteKeywords = ['remote', 'work from home', 'work-from-home', 'wfh', 'telecommute', 'telework', 'hybrid'];

    // Check location text
    if (locationText) {
      var locLower = locationText.toLowerCase();
      for (var i = 0; i < remoteKeywords.length; i++) {
        if (locLower.indexOf(remoteKeywords[i]) !== -1) {
          return true;
        }
      }
    }

    // Check for remote badges/attributes on the card
    var attributeSelectors = [
      '.attribute_snippet',
      '[class*="attribute"]',
      '[class*="Attribute"]',
      '.metadata',
      '[class*="metadata"]',
      '[class*="Metadata"]',
      '[class*="tag"]',
      '[class*="Tag"]',
      '[class*="badge"]',
      '[class*="Badge"]',
      '[class*="workType"]',
      '[class*="jobType"]',
      '[class*="remote"]',
      '[class*="Remote"]',
      '.jobMetaDataGroup',
      '[data-testid*="attribute"]',
      '[data-testid*="remote"]',
      '[aria-label*="remote"]',
      '[aria-label*="Remote"]'
    ];

    for (var j = 0; j < attributeSelectors.length; j++) {
      try {
        var elements = cardEl.querySelectorAll(attributeSelectors[j]);
        for (var k = 0; k < elements.length; k++) {
          var text = (elements[k].textContent || '').toLowerCase();
          for (var m = 0; m < remoteKeywords.length; m++) {
            if (text.indexOf(remoteKeywords[m]) !== -1) {
              return true;
            }
          }
        }
      } catch (e) {
        // Invalid selector, skip
      }
    }

    // Check SVG icons with remote-related aria-labels or titles
    var svgs = cardEl.querySelectorAll('svg');
    for (var n = 0; n < svgs.length; n++) {
      var svg = svgs[n];
      var ariaLabel = (svg.getAttribute('aria-label') || '').toLowerCase();
      var title = svg.querySelector('title');
      var titleText = title ? (title.textContent || '').toLowerCase() : '';

      for (var p = 0; p < remoteKeywords.length; p++) {
        if (ariaLabel.indexOf(remoteKeywords[p]) !== -1 || titleText.indexOf(remoteKeywords[p]) !== -1) {
          return true;
        }
      }
    }

    // Check the entire card text as a last resort, but be careful not to match false positives
    var fullText = (cardEl.textContent || '').toLowerCase();
    // Only match if "remote" appears as a standalone attribute (not part of a longer word)
    if (/\bremote\b/.test(fullText) || /\bhybrid\b/.test(fullText)) {
      return true;
    }

    return false;
  }

  function doExtract() {
    if (autoFetchEnabled) {
      console.log('[IND] Skipping extraction - auto-fetch is running');
      return;
    }
    if (availCheckEnabled) {
      console.log('[IND] Skipping extraction - availability check is running');
      return;
    }
    if (crawlEnabled) {
      console.log('[IND] Skipping extraction - crawl is running');
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

      var job = extractJobFromCard(card);
      if (job) {
        jobs.push(job);
      }
    });

    console.log('[IND] Extracted ' + jobs.length + ' unique jobs');

    // Get description for currently viewed job
    var currentDesc = getJobDescription();
    var currentUrl = getCurrentJobUrl();

    if (currentDesc && currentDesc.length > 50 && currentUrl) {
      console.log('[IND] Found description (' + currentDesc.length + ' chars) for current job');

      var normalizedCurrent = normalizeJobUrl(currentUrl);
      var foundInList = false;
      for (var k = 0; k < jobs.length; k++) {
        if (normalizeJobUrl(jobs[k].Url) === normalizedCurrent) {
          jobs[k].Description = currentDesc;
          foundInList = true;
          break;
        }
      }

      if (!foundInList) {
        updateDescription(normalizedCurrent, currentDesc);
      }
    }

    // If on job detail page, also add it if not in list
    var currentJobId = getCurrentJobId();
    if (currentJobId && !seenIds[currentJobId]) {
      var h1 = document.querySelector('h1') || document.querySelector('.jobsearch-JobInfoHeader-title');
      if (h1) {
        var detailTitle = cleanText(h1.textContent);
        if (detailTitle && detailTitle.length > 3) {
          var compEl = document.querySelector('[data-testid="inlineHeader-companyName"], .jobsearch-InlineCompanyRating-companyHeader, [class*="companyName"]');
          var locEl = document.querySelector('[data-testid="inlineHeader-companyLocation"], [class*="companyLocation"], [class*="location"]');
          var salEl = document.querySelector('[class*="salary"], .jobsearch-JobMetadataHeader-item');
          var detailLocation = locEl ? cleanText(locEl.textContent) : '';

          // Check remote status on the detail page
          var mainContent = document.querySelector('main') || document.querySelector('[role="main"]') || document.body;
          var detailIsRemote = checkIsRemote(mainContent, detailLocation);

          jobs.push({
            Title: detailTitle,
            Company: compEl ? cleanText(compEl.textContent) : '',
            Location: detailLocation,
            Description: currentDesc || '',
            JobType: 0,
            Salary: salEl ? cleanText(salEl.textContent) : '',
            Url: normalizeJobUrl(currentUrl),
            DatePosted: new Date().toISOString(),
            IsRemote: detailIsRemote,
            Skills: [],
            Source: 'Indeed'
          });
          console.log('[IND] Added detail view job: ' + detailTitle.substring(0, 30) + (detailIsRemote ? ' [REMOTE]' : ''));
        }
      }
    }

    // Filter out jobs already sent to server
    var newJobs = jobs.filter(function(j) { return !sentJobIds[j.Url]; });
    console.log('[IND] Total jobs: ' + jobs.length + ', new: ' + newJobs.length);

    if (newJobs.length > 0) {
      sendJobs(newJobs);
    } else {
      if (currentDesc && currentUrl) {
        updateDescription(normalizeJobUrl(currentUrl), currentDesc);
      }
      updateUI('Monitoring', totalSent);
    }
  }

  function sendJobs(jobs) {
    sending = true;
    var i = 0;

    function next() {
      if (i >= jobs.length) {
        sending = false;
        updateUI('Done', totalSent);
        setTimeout(function() { updateUI('Monitoring', totalSent); }, 2000);
        return;
      }

      var job = jobs[i];
      i++;
      updateUI('Sending ' + i + '/' + jobs.length, totalSent);

      console.log('[IND] Sending job:', {
        title: job.Title,
        company: job.Company,
        hasApiKey: !!API_KEY
      });

      fetch(SERVER_URL + '/api/jobs', {
        method: 'POST',
        headers: getHeaders(),
        body: JSON.stringify(job)
      })
      .then(function(r) {
        if (!r.ok) {
          return r.text().then(function(text) {
            console.log('[IND] Error response:', text.substring(0, 200));
            throw new Error('HTTP ' + r.status + ': ' + text.substring(0, 100));
          });
        }
        return r.json();
      })
      .then(function(d) {
        sentJobIds[job.Url] = true;
        if (d.added === true) {
          totalSent++;
          console.log('[IND] Added: ' + job.Title.substring(0, 40));
        } else {
          totalDuplicates++;
          if (job.Description && job.Description.length > 50) {
            updateDescription(job.Url, job.Description);
          }
        }
        next();
      })
      .catch(function(e) {
        console.log('[IND] Error: ' + e.message);
        next();
      });
    }

    next();
  }

  function checkAndFetchDescriptions() {
    if (fetchingDescriptions || sending) return;
    if (!window.location.href.includes('indeed.com')) return;

    fetchingDescriptions = true;

    fetch(SERVER_URL + '/api/jobs/needing-descriptions?limit=5', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          fetchingDescriptions = false;
          return;
        }

        console.log('[IND] Found ' + data.count + ' jobs needing descriptions');

        var currentUrl = normalizeJobUrl(getCurrentJobUrl());

        if (currentUrl) {
          var currentJobNeedsDesc = data.jobs.some(function(j) {
            return normalizeJobUrl(j.Url) === currentUrl;
          });

          if (currentJobNeedsDesc) {
            var desc = getJobDescription();
            if (desc && desc.length > 50) {
              console.log('[IND] Current job needs description - sending it now');
              updateDescription(currentUrl, desc);
            }
          }
        }

        fetchingDescriptions = false;
      })
      .catch(function(e) {
        console.log('[IND] Error checking for missing descriptions: ' + e.message);
        fetchingDescriptions = false;
      });
  }

  function startAutoFetch(delaySeconds) {
    if (autoFetchEnabled) {
      console.log('[IND] Auto-fetch already running');
      return;
    }

    autoFetchDelay = (delaySeconds || 3) * 1000;
    console.log('[IND] Starting auto-fetch with ' + (autoFetchDelay/1000) + 's delay');

    fetch(SERVER_URL + '/api/jobs/needing-descriptions', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          console.log('[IND] No jobs need descriptions!');
          alert('All jobs already have descriptions!');
          return;
        }

        // Filter for Indeed URLs only
        autoFetchQueue = data.jobs.filter(function(job) {
          var jobUrl = job.Url || job.url || '';
          return jobUrl.includes('indeed.com');
        }).map(function(job) {
          return {
            Url: normalizeJobUrl(job.Url || job.url || ''),
            Title: job.Title || job.title || 'Unknown Job',
            Company: job.Company || job.company || ''
          };
        });

        if (autoFetchQueue.length === 0) {
          console.log('[IND] No Indeed jobs need descriptions!');
          alert('No Indeed jobs need descriptions.');
          return;
        }

        autoFetchIndex = 0;
        autoFetchEnabled = true;

        sessionStorage.setItem('ind-autofetch', JSON.stringify({
          enabled: true,
          delay: autoFetchDelay,
          queue: autoFetchQueue,
          index: autoFetchIndex
        }));

        console.log('[IND] Auto-fetch queue: ' + autoFetchQueue.length + ' jobs');
        showAutoFetchUI();

        var firstJob = autoFetchQueue[0];
        updateAutoFetchUI(1, autoFetchQueue.length, firstJob.Title);

        navigateToNextJob();
      })
      .catch(function(e) {
        console.log('[IND] Error starting auto-fetch: ' + e.message);
        alert('Error starting auto-fetch: ' + e.message);
      });
  }

  function stopAutoFetch() {
    autoFetchEnabled = false;
    autoFetchQueue = [];
    autoFetchIndex = 0;
    sessionStorage.removeItem('ind-autofetch');
    hideAutoFetchUI();
    console.log('[IND] Auto-fetch stopped');
  }

  function checkAutoFetchState() {
    var state = sessionStorage.getItem('ind-autofetch');
    if (!state) return;

    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled && parsed.queue && parsed.queue.length > 0) {
        autoFetchEnabled = true;
        autoFetchDelay = parsed.delay || 3000;
        autoFetchQueue = parsed.queue;
        autoFetchIndex = parsed.index || 0;

        console.log('[IND] Resuming auto-fetch at job ' + (autoFetchIndex + 1) + ' of ' + autoFetchQueue.length);
        showAutoFetchUI();

        processCurrentPageForAutoFetch();
      }
    } catch (e) {
      console.log('[IND] Error restoring auto-fetch state: ' + e.message);
      sessionStorage.removeItem('ind-autofetch');
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

    setTimeout(function() {
      var desc = getJobDescription();
      var queueUrl = currentJob && currentJob.Url ? normalizeJobUrl(currentJob.Url) : '';

      console.log('[IND] Auto-fetch: About to update description', {
        queueUrl: queueUrl,
        descLength: desc ? desc.length : 0,
        descPreview: desc ? desc.substring(0, 80) : '',
        jobTitle: jobTitle
      });

      if (desc && desc.length > 50) {
        updateDescription(queueUrl, desc).then(function(result) {
          console.log('[IND] Auto-fetch: updateDescription result:', result);
          autoFetchIndex++;

          var state = JSON.parse(sessionStorage.getItem('ind-autofetch') || '{}');
          state.index = autoFetchIndex;
          sessionStorage.setItem('ind-autofetch', JSON.stringify(state));

          if (autoFetchIndex >= autoFetchQueue.length) {
            completeAutoFetch();
          } else {
            setTimeout(navigateToNextJob, autoFetchDelay);
          }
        });
      } else {
        console.log('[IND] Auto-fetch: No description found, skipping...');
        autoFetchIndex++;

        var state = JSON.parse(sessionStorage.getItem('ind-autofetch') || '{}');
        state.index = autoFetchIndex;
        sessionStorage.setItem('ind-autofetch', JSON.stringify(state));

        if (autoFetchIndex >= autoFetchQueue.length) {
          completeAutoFetch();
        } else {
          setTimeout(navigateToNextJob, 1000);
        }
      }
    }, 2000);
  }

  function navigateToNextJob() {
    if (!autoFetchEnabled || autoFetchIndex >= autoFetchQueue.length) {
      completeAutoFetch();
      return;
    }

    var job = autoFetchQueue[autoFetchIndex];
    var jobTitle = (job && job.Title) ? job.Title : 'Unknown Job';
    console.log('[IND] Auto-fetch: Navigating to job ' + (autoFetchIndex + 1) + ': ' + jobTitle.substring(0, 30));
    updateAutoFetchUI(autoFetchIndex + 1, autoFetchQueue.length, jobTitle);

    if (job && job.Url) {
      window.location.href = rewriteUrlToCurrentOrigin(job.Url);
    } else {
      console.log('[IND] Auto-fetch: Invalid job URL, skipping...');
      autoFetchIndex++;
      setTimeout(navigateToNextJob, 500);
    }
  }

  function completeAutoFetch() {
    var totalProcessed = autoFetchIndex;
    stopAutoFetch();
    console.log('[IND] Auto-fetch complete! Processed ' + totalProcessed + ' jobs');
    alert('Auto-fetch complete!\nProcessed ' + totalProcessed + ' jobs.');
  }

  // === Availability Check (client-side, navigates browser like auto-fetch) ===
  var availCheckEnabled = false;
  var availCheckDelay = 3000;
  var availCheckQueue = [];
  var availCheckIndex = 0;
  var availCheckResults = { checked: 0, markedUnavailable: 0, errors: 0 };

  function startAvailabilityCheck(delaySeconds) {
    availCheckDelay = (delaySeconds || 3) * 1000;
    if (availCheckEnabled || autoFetchEnabled) {
      console.log('[IND] Availability check or auto-fetch already running');
      return;
    }

    console.log('[IND] Starting availability check...');

    fetch(SERVER_URL + '/api/jobs/needing-availability-check?source=Indeed', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          console.log('[IND] No Indeed jobs need availability checking');
          alert('No Indeed jobs need checking (all checked recently).');
          return;
        }

        availCheckQueue = data.jobs.map(function(job) {
          return {
            Id: job.Id || job.id,
            Url: job.Url || job.url,
            Title: job.Title || job.title || 'Unknown Job',
            Company: job.Company || job.company || ''
          };
        });

        availCheckIndex = 0;
        availCheckEnabled = true;
        availCheckResults = { checked: 0, markedUnavailable: 0, errors: 0 };

        sessionStorage.setItem('ind-availcheck', JSON.stringify({
          enabled: true,
          delay: availCheckDelay,
          queue: availCheckQueue,
          index: availCheckIndex,
          results: availCheckResults
        }));

        console.log('[IND] Availability check queue: ' + availCheckQueue.length + ' jobs (' + (availCheckDelay/1000) + 's delay)');
        showAvailCheckUI();
        updateAvailCheckUI(1, availCheckQueue.length, availCheckQueue[0].Title);
        navigateToNextAvailCheck();
      })
      .catch(function(e) {
        console.log('[IND] Error starting availability check: ' + e.message);
        alert('Error: ' + e.message);
      });
  }

  function stopAvailabilityCheck() {
    availCheckEnabled = false;
    availCheckQueue = [];
    availCheckIndex = 0;
    sessionStorage.removeItem('ind-availcheck');
    hideAvailCheckUI();
    clearAvailCheckResume();
    console.log('[IND] Availability check stopped');
  }

  function checkAvailCheckState() {
    if (availCheckEnabled || autoFetchEnabled) return; // Already running
    var state = sessionStorage.getItem('ind-availcheck');
    if (!state) return;

    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled && parsed.queue && parsed.queue.length > 0) {
        availCheckEnabled = true;
        availCheckDelay = parsed.delay || 3000;
        availCheckQueue = parsed.queue;
        availCheckIndex = parsed.index || 0;
        availCheckResults = parsed.results || { checked: 0, markedUnavailable: 0, errors: 0 };

        console.log('[IND] Resuming availability check at job ' + (availCheckIndex + 1) + ' of ' + availCheckQueue.length);
        showAvailCheckUI();
        processCurrentPageForAvailCheck();
      }
    } catch (e) {
      console.log('[IND] Error restoring availability check state: ' + e.message);
      sessionStorage.removeItem('ind-availcheck');
    }
  }

  function processCurrentPageForAvailCheck() {
    if (!availCheckEnabled || availCheckIndex >= availCheckQueue.length) {
      completeAvailabilityCheck();
      return;
    }

    var currentJob = availCheckQueue[availCheckIndex];
    var jobTitle = currentJob.Title || 'Unknown Job';
    updateAvailCheckUI(availCheckIndex + 1, availCheckQueue.length, jobTitle);

    setTimeout(function() {
      // Check if we were redirected away from the job page (Indeed redirects expired jobs to search)
      var expectedKey = '';
      if (currentJob.Url) {
        var keyMatch = currentJob.Url.match(/jk=([a-f0-9]+)/i);
        if (keyMatch) expectedKey = keyMatch[1];
      }
      var currentUrl = window.location.href;
      var wasRedirected = expectedKey && currentUrl.indexOf(expectedKey) === -1;

      if (wasRedirected) {
        console.log('[IND] Job REDIRECTED (likely expired): ' + jobTitle + ' - expected jk=' + expectedKey + ' but on ' + currentUrl.substring(0, 80));
      }

      var bodyText = document.body ? (document.body.innerText || '').toLowerCase() : '';
      var isExpired = wasRedirected
                   || bodyText.indexOf('this job has expired') !== -1
                   || bodyText.indexOf('this job is no longer available') !== -1
                   || bodyText.indexOf('this position is no longer available') !== -1
                   || bodyText.indexOf('job expired') !== -1
                   || bodyText.indexOf('this job posting has expired') !== -1
                   || bodyText.indexOf('no longer accepting applications') !== -1;
      var is404 = bodyText.indexOf('the job you are looking for was not found') !== -1
               || bodyText.indexOf('we can\'t find that page') !== -1
               || bodyText.indexOf('page not found') !== -1
               || bodyText.indexOf('this page could not be found') !== -1;

      if (isExpired || is404) {
        console.log('[IND] Job EXPIRED: ' + jobTitle + ' (id=' + currentJob.Id + ')');
        availCheckResults.markedUnavailable++;
        var reason = wasRedirected ? 'Indeed: Job page redirected (expired)' : isExpired ? 'Indeed: This job has expired' : 'Indeed: Job page not found';
        fetch(SERVER_URL + '/api/jobs/' + currentJob.Id + '/mark-unavailable', {
          method: 'POST',
          keepalive: true,
          headers: getHeaders(),
          body: JSON.stringify({ reason: reason })
        })
        .then(function(r) {
          if (!r.ok) console.log('[IND] Error marking unavailable - HTTP ' + r.status);
          else console.log('[IND] Marked unavailable: ' + jobTitle);
        })
        .catch(function(e) { console.log('[IND] Error marking unavailable:', e); });
      } else {
        console.log('[IND] Job active: ' + jobTitle + ' (id=' + currentJob.Id + ')');
        fetch(SERVER_URL + '/api/jobs/' + currentJob.Id + '/mark-checked', {
          method: 'POST',
          keepalive: true,
          headers: getHeaders()
        })
        .then(function(r) {
          if (!r.ok) console.log('[IND] Error marking checked - HTTP ' + r.status);
        })
        .catch(function(e) { console.log('[IND] Error marking checked:', e); });
      }

      // Advance immediately - don't wait for API response
      availCheckResults.checked++;
      availCheckIndex++;

      // Write full state (not read-modify-write) to avoid losing fields
      sessionStorage.setItem('ind-availcheck', JSON.stringify({
        enabled: true,
        delay: availCheckDelay,
        queue: availCheckQueue,
        index: availCheckIndex,
        results: availCheckResults
      }));

      if (availCheckIndex >= availCheckQueue.length) {
        completeAvailabilityCheck();
      } else {
        console.log('[IND] Scheduling navigation to job ' + (availCheckIndex + 1) + ' in ' + (availCheckDelay/1000) + 's');
        setTimeout(navigateToNextAvailCheck, availCheckDelay);
      }
    }, 3000); // Wait 3s for page to fully render
  }

  function rewriteUrlToCurrentOrigin(url) {
    // Rewrite Indeed job URLs to use current page's origin to preserve sessionStorage
    try {
      var parsed = new URL(url);
      if (parsed.hostname.indexOf('indeed.com') !== -1) {
        return window.location.origin + parsed.pathname + parsed.search;
      }
    } catch (e) {}
    return url;
  }

  function navigateToNextAvailCheck() {
    if (!availCheckEnabled || availCheckIndex >= availCheckQueue.length) {
      completeAvailabilityCheck();
      return;
    }

    var job = availCheckQueue[availCheckIndex];
    console.log('[IND] Availability check: Navigating to job ' + (availCheckIndex + 1) + ': ' + (job.Title || '').substring(0, 30));
    updateAvailCheckUI(availCheckIndex + 1, availCheckQueue.length, job.Title);

    if (job && job.Url) {
      var targetUrl = rewriteUrlToCurrentOrigin(job.Url);
      console.log('[IND] Navigating to: ' + targetUrl);
      window.location.href = targetUrl;
    } else {
      console.log('[IND] No URL for job, skipping');
      availCheckResults.errors++;
      availCheckIndex++;
      setTimeout(navigateToNextAvailCheck, 500);
    }
  }

  function completeAvailabilityCheck() {
    var results = availCheckResults;
    stopAvailabilityCheck();
    var msg = 'Availability check complete!\nChecked ' + results.checked + ' jobs.';
    if (results.markedUnavailable > 0) msg += '\n' + results.markedUnavailable + ' marked unavailable.';
    if (results.errors > 0) msg += '\n' + results.errors + ' errors.';
    console.log('[IND] ' + msg);
    alert(msg);
  }

  function showAvailCheckUI() {
    var existing = document.getElementById('ind-availcheck-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'ind-availcheck-ui';
    el.innerHTML = '<div id="ind-ac-status">Checking job availability...</div>' +
                   '<div id="ind-ac-progress">Job <span id="ind-ac-current">0</span> of <span id="ind-ac-total">0</span></div>' +
                   '<div id="ind-ac-job"></div>' +
                   '<button id="ind-ac-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#b24020;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#ind-ac-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#ind-ac-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#ind-ac-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#ind-ac-stop').style.cssText = 'background:#7a2a15;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#ind-ac-stop').onclick = stopAvailabilityCheck;
    document.body.appendChild(el);
  }

  function updateAvailCheckUI(current, total, jobTitle) {
    var c = document.getElementById('ind-ac-current');
    var t = document.getElementById('ind-ac-total');
    var j = document.getElementById('ind-ac-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }

  function hideAvailCheckUI() {
    var el = document.getElementById('ind-availcheck-ui');
    if (el) el.remove();
  }

  // Check for availability check state on page load (resumes after navigation)
  // Use setInterval to catch any missed resumptions - stops itself once check is active
  var availCheckResumeInterval = setInterval(function() {
    if (availCheckEnabled) return; // Already running, no need to check
    checkAvailCheckState();
  }, 2000);

  function clearAvailCheckResume() {
    if (availCheckResumeInterval) {
      clearInterval(availCheckResumeInterval);
      availCheckResumeInterval = null;
    }
  }

  // === Crawl Functions (paginate through search results) ===
  var crawlEnabled = false;
  var crawlDelay = 5000;
  var crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
  var crawlMaxPages = 20;

  function startCrawl(delaySeconds, maxPages) {
    if (crawlEnabled || autoFetchEnabled || availCheckEnabled) {
      console.log('[IND] Crawl, auto-fetch or availability check already running');
      return;
    }
    crawlDelay = (delaySeconds || 5) * 1000;
    crawlMaxPages = maxPages || 20;
    crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
    crawlEnabled = true;

    sessionStorage.setItem('ind-crawl', JSON.stringify({
      enabled: true,
      delay: crawlDelay,
      maxPages: crawlMaxPages,
      results: crawlResults
    }));

    console.log('[IND] Starting crawl with ' + (crawlDelay/1000) + 's delay, max ' + crawlMaxPages + ' pages');
    showCrawlUI();
    processCrawlPage();
  }

  function stopCrawl() {
    crawlEnabled = false;
    sessionStorage.removeItem('ind-crawl');
    hideCrawlUI();
    console.log('[IND] Crawl stopped. Pages: ' + crawlResults.pagesScanned + ', Found: ' + crawlResults.jobsFound + ', Added: ' + crawlResults.jobsAdded);
  }

  function checkCrawlState() {
    if (crawlEnabled || autoFetchEnabled || availCheckEnabled) return;
    var state = sessionStorage.getItem('ind-crawl');
    if (!state) return;
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled) {
        crawlEnabled = true;
        crawlDelay = parsed.delay || 5000;
        crawlMaxPages = parsed.maxPages || 20;
        crawlResults = parsed.results || { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
        console.log('[IND] Resuming crawl - page ' + (crawlResults.pagesScanned + 1));
        showCrawlUI();
        processCrawlPage();
      }
    } catch (e) {
      sessionStorage.removeItem('ind-crawl');
    }
  }

  function processCrawlPage() {
    if (!crawlEnabled) return;
    if (crawlResults.pagesScanned >= crawlMaxPages) {
      completeCrawl();
      return;
    }

    updateCrawlUI();

    setTimeout(function() {
      if (!crawlEnabled) return;

      var cards = findAllJobCards();
      var pageJobs = [];
      var seenIds = {};

      cards.forEach(function(card) {
        if (seenIds[card.jobId]) return;
        seenIds[card.jobId] = true;
        var job = extractJobFromCard(card);
        if (job) pageJobs.push(job);
      });

      console.log('[IND] Crawl page ' + (crawlResults.pagesScanned + 1) + ': found ' + pageJobs.length + ' jobs');
      crawlResults.jobsFound += pageJobs.length;
      crawlResults.pagesScanned++;

      if (pageJobs.length === 0) {
        console.log('[IND] No jobs found on page, stopping crawl');
        completeCrawl();
        return;
      }

      crawlSendJobs(pageJobs, function(added) {
        crawlResults.jobsAdded += added;
        saveCrawlState();
        updateCrawlUI();

        if (crawlResults.pagesScanned >= crawlMaxPages) {
          completeCrawl();
          return;
        }

        var nextUrl = findNextPageUrlForCrawl();
        if (nextUrl) {
          console.log('[IND] Crawl: navigating to next page in ' + (crawlDelay/1000) + 's');
          setTimeout(function() {
            if (crawlEnabled) window.location.href = rewriteUrlToCurrentOrigin(nextUrl);
          }, crawlDelay);
        } else {
          console.log('[IND] Crawl: no next page found');
          completeCrawl();
        }
      });
    }, 3000);
  }

  function findNextPageUrlForCrawl() {
    // Indeed pagination: look for next page link
    var nextLink = document.querySelector('a[data-testid="pagination-page-next"]') ||
                   document.querySelector('a[aria-label="Next Page"]') ||
                   document.querySelector('.pagination-list li:last-child a') ||
                   document.querySelector('nav[role="navigation"] a[aria-label*="Next"]');

    if (nextLink && nextLink.href) return nextLink.href;

    // URL-based: increment start parameter
    var url = new URL(window.location.href);
    var start = parseInt(url.searchParams.get('start') || '0');
    url.searchParams.set('start', start + 10);
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
    sessionStorage.setItem('ind-crawl', JSON.stringify({
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
    console.log('[IND] ' + msg);
    alert(msg);
  }

  function showCrawlUI() {
    var existing = document.getElementById('ind-crawl-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'ind-crawl-ui';
    el.innerHTML = '<div id="ind-cr-status">Crawling search results...</div>' +
                   '<div id="ind-cr-progress">Page <span id="ind-cr-page">0</span> | Found <span id="ind-cr-found">0</span> | Added <span id="ind-cr-added">0</span></div>' +
                   '<button id="ind-cr-stop">Stop Crawl</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#057642;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#ind-cr-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#ind-cr-progress').style.cssText = 'margin-bottom:12px;';
    el.querySelector('#ind-cr-stop').style.cssText = 'background:#dc3545;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#ind-cr-stop').onclick = stopCrawl;
    document.body.appendChild(el);
  }

  function updateCrawlUI() {
    var p = document.getElementById('ind-cr-page');
    var f = document.getElementById('ind-cr-found');
    var a = document.getElementById('ind-cr-added');
    if (p) p.textContent = crawlResults.pagesScanned;
    if (f) f.textContent = crawlResults.jobsFound;
    if (a) a.textContent = crawlResults.jobsAdded;
  }

  function hideCrawlUI() {
    var el = document.getElementById('ind-crawl-ui');
    if (el) el.remove();
  }

  setTimeout(checkCrawlState, 2000);

  // Expose API
  window.IND = {
    extract: doExtract,
    status: function() { return { sent: totalSent, dup: totalDuplicates }; },
    test: function() {
      fetch(SERVER_URL + '/api/jobs', {
        headers: getHeaders()
      })
        .then(function(r) { return r.json(); })
        .then(function(d) { console.log('[IND] Server has ' + d.length + ' jobs'); })
        .catch(function(e) { console.log('[IND] Error: ' + e.message); });
    },
    getDesc: function() {
      var desc = getJobDescription();
      console.log('[IND] Current URL:', getCurrentJobUrl());
      console.log('[IND] Current Job ID:', getCurrentJobId());
      console.log('[IND] Description (' + desc.length + ' chars):', desc.substring(0, 300));
      return desc;
    },
    updateDesc: function() {
      var desc = getJobDescription();
      var url = normalizeJobUrl(getCurrentJobUrl());
      if (desc && url) {
        updateDescription(url, desc);
        console.log('[IND] Sent description update');
      } else {
        console.log('[IND] No description or URL found');
      }
    },
    fetchMissing: checkAndFetchDescriptions,
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
    checkAvailability: startAvailabilityCheck,
    stopAvailCheck: stopAvailabilityCheck,
    availCheckStatus: function() {
      return {
        enabled: availCheckEnabled,
        total: availCheckQueue.length,
        current: availCheckIndex,
        results: availCheckResults
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
      console.log('[IND] === DEBUG ===');
      console.log('[IND] URL:', window.location.href);
      console.log('[IND] Server URL:', SERVER_URL);
      console.log('[IND] API Key:', API_KEY ? '(set)' : '(not set)');
      console.log('[IND] Current Job ID:', getCurrentJobId());

      console.log('[IND] Checking selectors...');
      var cards = findAllJobCards();
      console.log('[IND] Total cards found:', cards.length);

      if (cards.length > 0) {
        console.log('[IND] First 3 jobs:');
        for (var i = 0; i < Math.min(3, cards.length); i++) {
          var job = extractJobFromCard(cards[i]);
          console.log('  ' + (i+1) + '. "' + (job ? job.Title.substring(0, 40) : 'NO TITLE') + '" at "' + (job ? job.Company : 'NO COMPANY') + '"');
        }
      }

      console.log('[IND] Description length:', getJobDescription().length);
      console.log('[IND] Auto-fetch status:', window.IND.autoFetchStatus());

      // Log page structure for debugging selectors
      console.log('[IND] Page structure hints:');
      console.log('  h1:', document.querySelector('h1') ? document.querySelector('h1').textContent.substring(0, 50) : 'none');
      console.log('  [data-jk] count:', document.querySelectorAll('[data-jk]').length);
      console.log('  .job_seen_beacon count:', document.querySelectorAll('.job_seen_beacon').length);
      console.log('  .jobsearch-ResultsList li count:', document.querySelectorAll('.jobsearch-ResultsList > li').length);
      console.log('  .tapItem count:', document.querySelectorAll('.tapItem').length);

      fetch(SERVER_URL + '/api/jobs/needing-descriptions?limit=5', {
        headers: getHeaders()
      })
        .then(function(r) { return r.json(); })
        .then(function(d) {
          var indeedJobs = d.jobs ? d.jobs.filter(function(j) { return (j.Url || j.url || '').includes('indeed.com'); }) : [];
          console.log('[IND] Indeed jobs needing descriptions:', indeedJobs.length);
        })
        .catch(function(e) { console.log('[IND] Error checking: ' + e.message); });
    },
    help: function() {
      console.log('[IND] === COMMANDS ===');
      console.log('IND.extract()         - Extract jobs from current page');
      console.log('IND.debug()           - Show debug info');
      console.log('IND.test()            - Test server connection');
      console.log('IND.status()          - Show send statistics');
      console.log('IND.getDesc()         - Get current job description');
      console.log('IND.updateDesc()      - Send current description to server');
      console.log('IND.fetchMissing()    - Check for jobs needing descriptions');
      console.log('');
      console.log('=== AUTO-FETCH ===');
      console.log('IND.autoFetch(3)      - Start auto-fetching descriptions (3s delay)');
      console.log('IND.stopAutoFetch()   - Stop auto-fetching');
      console.log('IND.autoFetchStatus() - Check auto-fetch progress');
      console.log('');
      console.log('=== AVAILABILITY CHECK ===');
      console.log('IND.checkAvailability()  - Check if jobs are still active (navigates browser)');
      console.log('IND.stopAvailCheck()     - Stop availability check');
      console.log('IND.availCheckStatus()   - Check availability check progress');
      console.log('');
      console.log('=== CRAWL ===');
      console.log('IND.crawl(5, 20)      - Crawl search results (5s delay, 20 pages max)');
      console.log('IND.stopCrawl()       - Stop crawling');
      console.log('IND.crawlStatus()     - Check crawl progress');
    }
  };

  console.log('[IND] Ready! Type IND.help() for commands.');
})();
