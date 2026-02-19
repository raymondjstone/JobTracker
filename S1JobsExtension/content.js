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

  console.log('[S1J] S1Jobs Extractor loaded');

  // Load server URL and API key from storage
  if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
      if (result.serverUrl) {
        SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        console.log('[S1J] Using server URL from settings:', SERVER_URL);
      }
      if (result.apiKey) {
        API_KEY = result.apiKey;
        console.log('[S1J] API key loaded');
      }
    });
  }

  // Helper function to get headers with API key
  function getHeaders() {
    var headers = { 'Content-Type': 'application/json' };
    if (API_KEY) {
      headers['X-API-Key'] = API_KEY;
    }
    return headers;
  }

  // Mark that extension is installed
  window.S1JobsExtractorInstalled = true;
  document.documentElement.setAttribute('data-s1j-installed', 'true');

  createUI();
  setTimeout(doExtract, 2000);

  window.addEventListener('scroll', function() {
    setTimeout(doExtract, 1000);
  });

  setInterval(doExtract, 5000);
  setInterval(checkAndFetchDescriptions, 30000);
  setTimeout(checkAndFetchDescriptions, 5000);
  setTimeout(checkAutoFetchState, 1500);

  function createUI() {
    if (document.getElementById('s1j-ui')) return;
    var el = document.createElement('div');
    el.id = 's1j-ui';
    el.innerHTML = '<span id="s1j-dot"></span><span id="s1j-text">Ready</span><span id="s1j-count">0</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#0072CE;color:#fff;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#s1j-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#s1j-count').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;';
    document.body.appendChild(el);
    el.onclick = doExtract;
  }

  function updateUI(text, count) {
    var t = document.getElementById('s1j-text');
    var c = document.getElementById('s1j-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }

  function showAutoFetchUI() {
    var existing = document.getElementById('s1j-autofetch-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 's1j-autofetch-ui';
    el.innerHTML = '<div id="s1j-af-status">Auto-fetching descriptions...</div>' +
                   '<div id="s1j-af-progress">Job <span id="s1j-af-current">0</span> of <span id="s1j-af-total">0</span></div>' +
                   '<div id="s1j-af-job"></div>' +
                   '<button id="s1j-af-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#0072CE;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#s1j-af-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#s1j-af-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#s1j-af-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#s1j-af-stop').style.cssText = 'background:#005ba3;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#s1j-af-stop').onclick = stopAutoFetch;
    document.body.appendChild(el);
  }

  function updateAutoFetchUI(current, total, jobTitle) {
    var c = document.getElementById('s1j-af-current');
    var t = document.getElementById('s1j-af-total');
    var j = document.getElementById('s1j-af-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }

  function hideAutoFetchUI() {
    var el = document.getElementById('s1j-autofetch-ui');
    if (el) el.remove();
  }

  function cleanText(text) {
    if (!text) return '';
    text = text.trim().replace(/\s+/g, ' ').replace(/\n/g, ' ');
    return text.trim();
  }

  function getJobDescription() {
    // First, try JSON-LD structured data (most reliable for S1Jobs)
    var jsonLdScripts = document.querySelectorAll('script[type="application/ld+json"]');
    for (var k = 0; k < jsonLdScripts.length; k++) {
      try {
        var jsonText = jsonLdScripts[k].textContent;
        var data = JSON.parse(jsonText);

        if (Array.isArray(data)) {
          data = data[0];
        }

        if (data['@graph'] && Array.isArray(data['@graph'])) {
          for (var g = 0; g < data['@graph'].length; g++) {
            if (data['@graph'][g].description && data['@graph'][g].description.length > 50) {
              console.log('[S1J] Found description from JSON-LD @graph');
              return data['@graph'][g].description.substring(0, 10000);
            }
          }
        }

        if (data.description && data.description.length > 50) {
          console.log('[S1J] Found description from JSON-LD');
          return data.description.substring(0, 10000);
        }
      } catch (e) {
        console.log('[S1J] JSON-LD parse error: ' + e.message);
      }
    }

    var descSelectors = [
      '.job-detail-description',
      '.vacancy-content',
      '.job-description-content',
      '[data-testid="job-description"]',
      '.job-description',
      '.job-detail__description',
      '.vacancy-description',
      '.job-content',
      '#job-description',
      'section[class*="description"]',
      'article[class*="description"]',
      '[class*="job-description"]',
      '.job-details-content',
      'article.job-detail',
      '.job-body',
      'main [class*="description"]'
    ];

    for (var i = 0; i < descSelectors.length; i++) {
      try {
        var el = document.querySelector(descSelectors[i]);
        if (el && el.innerText && el.innerText.trim().length > 50) {
          console.log('[S1J] Found description using selector: ' + descSelectors[i]);
          return el.innerText.trim().substring(0, 10000);
        }
      } catch (e) {}
    }

    var mainContent = document.querySelector('main') ||
                      document.querySelector('[role="main"]') ||
                      document.querySelector('.main-content') ||
                      document.querySelector('.job-detail');

    if (mainContent) {
      var allElements = mainContent.querySelectorAll('div, section, article, p');
      var bestText = '';
      var bestScore = 0;

      for (var j = 0; j < allElements.length; j++) {
        var text = allElements[j].innerText;
        if (!text) continue;

        text = text.trim();
        if (text.length < 200 || text.length > 20000) continue;

        var score = 0;
        var lowerText = text.toLowerCase();

        if (lowerText.includes('responsibilities')) score += 20;
        if (lowerText.includes('requirements')) score += 20;
        if (lowerText.includes('experience')) score += 15;
        if (lowerText.includes('skills')) score += 15;
        if (text.length > 500) score += 10;

        if (score > bestScore) {
          bestScore = score;
          bestText = text;
        }
      }

      if (bestText && bestScore >= 20) {
        console.log('[S1J] Found description using fallback (score: ' + bestScore + ')');
        return bestText.substring(0, 10000);
      }
    }

    return '';
  }

  function getCurrentJobId() {
    var s1jobsMatch = window.location.href.match(/\/job\/[^\/]+-(\d+)(?:$|\/|\?)/);
    if (s1jobsMatch) return s1jobsMatch[1];

    var patterns = [/\/job\/(\d+)/, /\/jobs\/(\d+)/, /[?&]id=(\d+)/, /[?&]jobId=(\d+)/];
    for (var i = 0; i < patterns.length; i++) {
      var match = window.location.href.match(patterns[i]);
      if (match) return match[1];
    }
    return '';
  }

  function getCurrentJobUrl() {
    var canonical = document.querySelector('link[rel="canonical"]');
    if (canonical && canonical.href) {
      return canonical.href;
    }
    return window.location.href.split('?')[0];
  }

  function normalizeJobUrl(url) {
    if (!url) return '';
    return url.replace(/\/$/, '').split('?')[0].toLowerCase();
  }

  function updateDescription(url, description) {
    if (!url || !description || description.length < 50) {
      return Promise.resolve(false);
    }

    var normalizedUrl = normalizeJobUrl(url);
    console.log('[S1J] Updating description for URL:', normalizedUrl);

    return fetch(SERVER_URL + '/api/jobs/description', {
      method: 'PUT',
      headers: getHeaders(),
      body: JSON.stringify({ Url: normalizedUrl, Description: description })
    })
    .then(function(r) { return r.json(); })
    .then(function(d) {
      if (d.updated) {
        console.log('[S1J] Description updated successfully');
        return true;
      }
      return false;
    })
    .catch(function(e) {
      console.log('[S1J] Error updating description: ' + e.message);
      return false;
    });
  }

  function findAllJobCards() {
    var cards = [];
    var cardSelectors = [
      '.job-card',
      '.job-listing',
      '.job-result',
      '.vacancy-card',
      '[class*="job-card"]',
      '[class*="job-listing"]',
      'article[class*="job"]',
      '.job-item',
      'li[class*="job"]'
    ];

    for (var s = 0; s < cardSelectors.length; s++) {
      try {
        var foundCards = document.querySelectorAll(cardSelectors[s]);
        if (foundCards.length > 0) {
          foundCards.forEach(function(card) {
            var link = card.querySelector('a[href*="job"]') || card.querySelector('a');
            if (link && link.href) {
              var jobId = extractJobIdFromUrl(link.href);
              if (jobId) {
                cards.push({ element: card, jobId: jobId, url: link.href });
              }
            }
          });
          if (cards.length > 0) break;
        }
      } catch (e) {}
    }

    if (cards.length === 0) {
      var allLinks = document.querySelectorAll('a[href*="job"]');
      allLinks.forEach(function(link) {
        var jobId = extractJobIdFromUrl(link.href);
        if (jobId) {
          var container = link.closest('li') || link.closest('article') || link.closest('div');
          cards.push({ element: container || link, jobId: jobId, url: link.href });
        }
      });
    }

    console.log('[S1J] Found ' + cards.length + ' job cards');
    return cards;
  }

  function extractJobIdFromUrl(url) {
    if (!url) return '';
    var s1jobsMatch = url.match(/\/job\/[^\/]+-(\d+)(?:$|\/|\?)/);
    if (s1jobsMatch) return s1jobsMatch[1];

    var patterns = [/\/job\/([^\/\?]+)/, /\/jobs\/([^\/\?]+)/, /[?&]id=([^&]+)/];
    for (var i = 0; i < patterns.length; i++) {
      var match = url.match(patterns[i]);
      if (match) return match[1];
    }
    return '';
  }

  function extractJobFromCard(card) {
    var el = card.element;
    var url = normalizeJobUrl(card.url);

    var title = '';
    var titleSelectors = ['.job-title', '.vacancy-title', 'h2', 'h3', '[class*="title"]', 'a[href*="job"]', 'strong', 'a'];
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

    if (!title || title.length < 3) return null;

    var company = '';
    var companySelectors = ['.company-name', '.employer', '.company', '[class*="company"]'];
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

    var location = '';
    var locSelectors = ['.location', '.job-location', '[class*="location"]'];
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

    var salary = '';
    var salarySelectors = ['.salary', '.job-salary', '[class*="salary"]'];
    for (var m = 0; m < salarySelectors.length; m++) {
      var salEl = el.querySelector(salarySelectors[m]);
      if (salEl) {
        salary = cleanText(salEl.textContent);
        break;
      }
    }

    return {
      Title: title,
      Company: company,
      Location: location,
      Description: '',
      JobType: 0,
      Salary: salary,
      Url: url,
      DatePosted: new Date().toISOString(),
      IsRemote: location.toLowerCase().indexOf('remote') !== -1,
      Skills: [],
      Source: 'S1Jobs'
    };
  }

  function doExtract() {
    if (autoFetchEnabled || crawlEnabled) return;
    if (sending) return;
    updateUI('Scanning', totalSent);

    var jobs = [];
    var seenIds = {};

    var cards = findAllJobCards();
    cards.forEach(function(card) {
      if (seenIds[card.jobId]) return;
      seenIds[card.jobId] = true;

      var job = extractJobFromCard(card);
      if (job) jobs.push(job);
    });

    console.log('[S1J] Extracted ' + jobs.length + ' unique jobs');

    var currentDesc = getJobDescription();
    var currentUrl = getCurrentJobUrl();

    if (currentDesc && currentDesc.length > 50 && currentUrl) {
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
        updateDescription(currentUrl, currentDesc);
      }
    }

    var currentJobId = getCurrentJobId();
    if (currentJobId && !seenIds[currentJobId]) {
      var h1 = document.querySelector('h1');
      if (h1) {
        var detailTitle = cleanText(h1.textContent);
        if (detailTitle && detailTitle.length > 3) {
          var compEl = document.querySelector('.company-name, .employer, [class*="company"]');
          var locEl = document.querySelector('.location, [class*="location"]');
          var salEl = document.querySelector('.salary, [class*="salary"]');

          jobs.push({
            Title: detailTitle,
            Company: compEl ? cleanText(compEl.textContent) : '',
            Location: locEl ? cleanText(locEl.textContent) : '',
            Description: currentDesc || '',
            JobType: 0,
            Salary: salEl ? cleanText(salEl.textContent) : '',
            Url: currentUrl,
            DatePosted: new Date().toISOString(),
            IsRemote: false,
            Skills: [],
            Source: 'S1Jobs'
          });
        }
      }
    }

    // Filter out jobs already sent to server
    var newJobs = jobs.filter(function(j) { return !sentJobIds[j.Url]; });
    console.log('[S1J] Total jobs: ' + jobs.length + ', new: ' + newJobs.length);

    if (newJobs.length > 0) {
      sendJobs(newJobs);
    } else {
      if (currentDesc && currentUrl) {
        updateDescription(currentUrl, currentDesc);
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

      fetch(SERVER_URL + '/api/jobs', {
        method: 'POST',
        headers: getHeaders(),
        body: JSON.stringify(job)
      })
      .then(function(r) { return r.json(); })
      .then(function(d) {
        sentJobIds[job.Url] = true;
        if (d.added === true) {
          totalSent++;
          console.log('[S1J] Added: ' + job.Title.substring(0, 40));
        } else {
          totalDuplicates++;
          if (job.Description && job.Description.length > 50) {
            updateDescription(job.Url, job.Description);
          }
        }
        next();
      })
      .catch(function(e) {
        console.log('[S1J] Error: ' + e.message);
        next();
      });
    }

    next();
  }

  function checkAndFetchDescriptions() {
    if (fetchingDescriptions || sending) return;
    if (!window.location.href.includes('s1jobs.com')) return;

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

        var currentUrl = normalizeJobUrl(getCurrentJobUrl());
        if (currentUrl) {
          var currentJobNeedsDesc = data.jobs.some(function(j) {
            return normalizeJobUrl(j.Url) === currentUrl;
          });

          if (currentJobNeedsDesc) {
            var desc = getJobDescription();
            if (desc && desc.length > 50) {
              updateDescription(currentUrl, desc);
            }
          }
        }

        fetchingDescriptions = false;
      })
      .catch(function(e) {
        console.log('[S1J] Error checking for missing descriptions: ' + e.message);
        fetchingDescriptions = false;
      });
  }

  function startAutoFetch(delaySeconds) {
    if (autoFetchEnabled) return;

    autoFetchDelay = (delaySeconds || 3) * 1000;

    fetch(SERVER_URL + '/api/jobs/needing-descriptions', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          alert('All jobs already have descriptions!');
          return;
        }

        autoFetchQueue = data.jobs.filter(function(job) {
          var jobUrl = job.Url || job.url || '';
          return jobUrl.includes('s1jobs.com');
        }).map(function(job) {
          return {
            Url: normalizeJobUrl(job.Url || job.url || ''),
            Title: job.Title || job.title || 'Unknown Job',
            Company: job.Company || job.company || ''
          };
        });

        if (autoFetchQueue.length === 0) {
          alert('No S1Jobs jobs need descriptions.');
          return;
        }

        autoFetchIndex = 0;
        autoFetchEnabled = true;

        sessionStorage.setItem('s1j-autofetch', JSON.stringify({
          enabled: true,
          delay: autoFetchDelay,
          queue: autoFetchQueue,
          index: autoFetchIndex
        }));

        showAutoFetchUI();
        updateAutoFetchUI(1, autoFetchQueue.length, autoFetchQueue[0].Title);
        navigateToNextJob();
      })
      .catch(function(e) {
        alert('Error starting auto-fetch: ' + e.message);
      });
  }

  function stopAutoFetch() {
    autoFetchEnabled = false;
    autoFetchQueue = [];
    autoFetchIndex = 0;
    sessionStorage.removeItem('s1j-autofetch');
    hideAutoFetchUI();
  }

  function checkAutoFetchState() {
    var state = sessionStorage.getItem('s1j-autofetch');
    if (!state) return;

    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled && parsed.queue && parsed.queue.length > 0) {
        autoFetchEnabled = true;
        autoFetchDelay = parsed.delay || 3000;
        autoFetchQueue = parsed.queue;
        autoFetchIndex = parsed.index || 0;

        showAutoFetchUI();
        processCurrentPageForAutoFetch();
      }
    } catch (e) {
      sessionStorage.removeItem('s1j-autofetch');
    }
  }

  function processCurrentPageForAutoFetch() {
    if (!autoFetchEnabled || autoFetchIndex >= autoFetchQueue.length) {
      completeAutoFetch();
      return;
    }

    var currentJob = autoFetchQueue[autoFetchIndex];
    updateAutoFetchUI(autoFetchIndex + 1, autoFetchQueue.length, currentJob.Title);

    setTimeout(function() {
      var desc = getJobDescription();
      var urlToUpdate = getCurrentJobUrl() || currentJob.Url;

      if (desc && desc.length > 50) {
        updateDescription(urlToUpdate, desc).then(function() {
          autoFetchIndex++;
          var state = JSON.parse(sessionStorage.getItem('s1j-autofetch') || '{}');
          state.index = autoFetchIndex;
          sessionStorage.setItem('s1j-autofetch', JSON.stringify(state));

          if (autoFetchIndex >= autoFetchQueue.length) {
            completeAutoFetch();
          } else {
            setTimeout(navigateToNextJob, autoFetchDelay);
          }
        });
      } else {
        autoFetchIndex++;
        var state = JSON.parse(sessionStorage.getItem('s1j-autofetch') || '{}');
        state.index = autoFetchIndex;
        sessionStorage.setItem('s1j-autofetch', JSON.stringify(state));

        if (autoFetchIndex >= autoFetchQueue.length) {
          completeAutoFetch();
        } else {
          setTimeout(navigateToNextJob, 1000);
        }
      }
    }, 3000);
  }

  function navigateToNextJob() {
    if (!autoFetchEnabled || autoFetchIndex >= autoFetchQueue.length) {
      completeAutoFetch();
      return;
    }

    var job = autoFetchQueue[autoFetchIndex];
    updateAutoFetchUI(autoFetchIndex + 1, autoFetchQueue.length, job.Title);

    if (job && job.Url) {
      window.location.href = job.Url;
    } else {
      autoFetchIndex++;
      setTimeout(navigateToNextJob, 500);
    }
  }

  function completeAutoFetch() {
    var totalProcessed = autoFetchIndex;
    stopAutoFetch();
    alert('Auto-fetch complete!\nProcessed ' + totalProcessed + ' jobs.');
  }

  // === Crawl Functions (paginate through search results) ===
  var crawlEnabled = false;
  var crawlDelay = 5000;
  var crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
  var crawlMaxPages = 20;

  function startCrawl(delaySeconds, maxPages) {
    if (crawlEnabled || autoFetchEnabled) {
      console.log('[S1J] Crawl or auto-fetch already running');
      return;
    }
    crawlDelay = (delaySeconds || 5) * 1000;
    crawlMaxPages = maxPages || 20;
    crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
    crawlEnabled = true;

    sessionStorage.setItem('s1j-crawl', JSON.stringify({
      enabled: true,
      delay: crawlDelay,
      maxPages: crawlMaxPages,
      results: crawlResults
    }));

    console.log('[S1J] Starting crawl with ' + (crawlDelay/1000) + 's delay, max ' + crawlMaxPages + ' pages');
    showCrawlUI();
    processCrawlPage();
  }

  function stopCrawl() {
    crawlEnabled = false;
    sessionStorage.removeItem('s1j-crawl');
    hideCrawlUI();
    console.log('[S1J] Crawl stopped. Pages: ' + crawlResults.pagesScanned + ', Found: ' + crawlResults.jobsFound + ', Added: ' + crawlResults.jobsAdded);
  }

  function checkCrawlState() {
    var state = sessionStorage.getItem('s1j-crawl');
    if (!state) return;
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled) {
        crawlEnabled = true;
        crawlDelay = parsed.delay || 5000;
        crawlMaxPages = parsed.maxPages || 20;
        crawlResults = parsed.results || { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
        console.log('[S1J] Resuming crawl - page ' + (crawlResults.pagesScanned + 1));
        showCrawlUI();
        processCrawlPage();
      }
    } catch (e) {
      sessionStorage.removeItem('s1j-crawl');
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

      console.log('[S1J] Crawl page ' + (crawlResults.pagesScanned + 1) + ': found ' + pageJobs.length + ' jobs');
      crawlResults.jobsFound += pageJobs.length;
      crawlResults.pagesScanned++;

      if (pageJobs.length === 0) {
        console.log('[S1J] No jobs found on page, stopping crawl');
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

        var nextUrl = findNextPageUrl();
        if (nextUrl) {
          console.log('[S1J] Crawl: navigating to next page in ' + (crawlDelay/1000) + 's');
          setTimeout(function() {
            if (crawlEnabled) window.location.href = nextUrl;
          }, crawlDelay);
        } else {
          console.log('[S1J] Crawl: no next page found');
          completeCrawl();
        }
      });
    }, 2000);
  }

  function findNextPageUrl() {
    // S1Jobs pagination: look for next page link
    var nextLink = document.querySelector('a[rel="next"]') ||
                   document.querySelector('.pagination .next a') ||
                   document.querySelector('a[aria-label="Next"]') ||
                   document.querySelector('.pagination li.active + li a');
    if (nextLink && nextLink.href) return nextLink.href;

    // URL-based: increment page parameter
    var url = new URL(window.location.href);
    var page = parseInt(url.searchParams.get('page') || '1');
    url.searchParams.set('page', page + 1);
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
    sessionStorage.setItem('s1j-crawl', JSON.stringify({
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
    console.log('[S1J] ' + msg);
    alert(msg);
  }

  function showCrawlUI() {
    var existing = document.getElementById('s1j-crawl-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 's1j-crawl-ui';
    el.innerHTML = '<div id="s1j-cr-status">Crawling search results...</div>' +
                   '<div id="s1j-cr-progress">Page <span id="s1j-cr-page">0</span> | Found <span id="s1j-cr-found">0</span> | Added <span id="s1j-cr-added">0</span></div>' +
                   '<button id="s1j-cr-stop">Stop Crawl</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#057642;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#s1j-cr-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#s1j-cr-progress').style.cssText = 'margin-bottom:12px;';
    el.querySelector('#s1j-cr-stop').style.cssText = 'background:#dc3545;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#s1j-cr-stop').onclick = stopCrawl;
    document.body.appendChild(el);
  }

  function updateCrawlUI() {
    var p = document.getElementById('s1j-cr-page');
    var f = document.getElementById('s1j-cr-found');
    var a = document.getElementById('s1j-cr-added');
    if (p) p.textContent = crawlResults.pagesScanned;
    if (f) f.textContent = crawlResults.jobsFound;
    if (a) a.textContent = crawlResults.jobsAdded;
  }

  function hideCrawlUI() {
    var el = document.getElementById('s1j-crawl-ui');
    if (el) el.remove();
  }

  setTimeout(checkCrawlState, 2000);

  window.S1J = {
    extract: doExtract,
    status: function() { return { sent: totalSent, dup: totalDuplicates }; },
    test: function() {
      fetch(SERVER_URL + '/api/jobs', { headers: getHeaders() })
        .then(function(r) { return r.json(); })
        .then(function(d) { console.log('[S1J] Server has ' + d.length + ' jobs'); })
        .catch(function(e) { console.log('[S1J] Error: ' + e.message); });
    },
    getDesc: function() {
      var desc = getJobDescription();
      console.log('[S1J] Description (' + desc.length + ' chars):', desc.substring(0, 300));
      return desc;
    },
    updateDesc: function() {
      var desc = getJobDescription();
      var url = getCurrentJobUrl();
      if (desc && url) {
        updateDescription(url, desc);
      }
    },
    fetchMissing: checkAndFetchDescriptions,
    autoFetch: function(delaySeconds) { startAutoFetch(delaySeconds || 3); },
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
      console.log('[S1J] === DEBUG ===');
      console.log('[S1J] URL:', window.location.href);
      console.log('[S1J] Server URL:', SERVER_URL);
      console.log('[S1J] API Key:', API_KEY ? '(set)' : '(not set)');
      console.log('[S1J] Current Job ID:', getCurrentJobId());
      console.log('[S1J] Description length:', getJobDescription().length);
    },
    help: function() {
      console.log('[S1J] === COMMANDS ===');
      console.log('S1J.extract()         - Extract jobs from current page');
      console.log('S1J.debug()           - Show debug info');
      console.log('S1J.test()            - Test server connection');
      console.log('S1J.autoFetch(3)      - Start auto-fetching descriptions');
      console.log('S1J.stopAutoFetch()   - Stop auto-fetching');
      console.log('');
      console.log('=== CRAWL ===');
      console.log('S1J.crawl(5, 20)      - Crawl search results (5s delay, 20 pages max)');
      console.log('S1J.stopCrawl()       - Stop crawling');
      console.log('S1J.crawlStatus()     - Check crawl progress');
    }
  };

  console.log('[S1J] Ready! Type S1J.help() for commands.');
})();
