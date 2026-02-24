(function() {
  var SERVER_URL = 'https://localhost:7046';
  var API_KEY = '';
  var totalSent = 0;
  var totalDuplicates = 0;
  var sending = false;
  var fetchingDescriptions = false;
  var sentJobIds = {};

  // Auto-fetch settings
  var autoFetchEnabled = false;
  var autoFetchDelay = 3000;
  var autoFetchQueue = [];
  var autoFetchIndex = 0;

  console.log('[EJS] EnergyJobSearch Extractor loaded');

  // Load server URL and API key from storage
  if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
      if (result.serverUrl) {
        SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        console.log('[EJS] Using server URL from settings:', SERVER_URL);
      }
      if (result.apiKey) {
        API_KEY = result.apiKey;
        console.log('[EJS] API key loaded');
      }
    });
  }

  function getHeaders() {
    var headers = { 'Content-Type': 'application/json' };
    if (API_KEY) {
      headers['X-API-Key'] = API_KEY;
    }
    return headers;
  }

  // Mark that extension is installed
  window.EnergyJobSearchExtractorInstalled = true;
  document.documentElement.setAttribute('data-ejs-installed', 'true');

  createUI();
  setTimeout(doExtract, 3000); // Longer delay for React SPA

  window.addEventListener('scroll', function() {
    setTimeout(doExtract, 1500);
  });

  setInterval(doExtract, 6000);
  setInterval(checkAndFetchDescriptions, 30000);
  setTimeout(checkAndFetchDescriptions, 6000);
  setTimeout(checkAutoFetchState, 2000);

  function createUI() {
    if (document.getElementById('ejs-ui')) return;
    var el = document.createElement('div');
    el.id = 'ejs-ui';
    el.innerHTML = '<span id="ejs-dot"></span><span id="ejs-text">Ready</span><span id="ejs-count">0</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#FF6B00;color:#fff;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#ejs-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#ejs-count').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;';
    document.body.appendChild(el);
    el.onclick = doExtract;
  }

  function updateUI(text, count) {
    var t = document.getElementById('ejs-text');
    var c = document.getElementById('ejs-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }

  function showAutoFetchUI() {
    var existing = document.getElementById('ejs-autofetch-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'ejs-autofetch-ui';
    el.innerHTML = '<div id="ejs-af-status">Auto-fetching descriptions...</div>' +
                   '<div id="ejs-af-progress">Job <span id="ejs-af-current">0</span> of <span id="ejs-af-total">0</span></div>' +
                   '<div id="ejs-af-job"></div>' +
                   '<button id="ejs-af-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#FF6B00;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#ejs-af-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#ejs-af-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#ejs-af-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#ejs-af-stop').style.cssText = 'background:#cc5500;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#ejs-af-stop').onclick = stopAutoFetch;
    document.body.appendChild(el);
  }

  function updateAutoFetchUI(current, total, jobTitle) {
    var c = document.getElementById('ejs-af-current');
    var t = document.getElementById('ejs-af-total');
    var j = document.getElementById('ejs-af-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }

  function hideAutoFetchUI() {
    var el = document.getElementById('ejs-autofetch-ui');
    if (el) el.remove();
  }

  function cleanText(text) {
    if (!text) return '';
    text = text.trim().replace(/\s+/g, ' ').replace(/\n/g, ' ');
    return text.trim();
  }

  function stripHtml(html) {
    if (!html) return '';
    // Convert block elements to newlines
    html = html.replace(/<br\s*\/?>/gi, '\n');
    html = html.replace(/<\/(?:p|div|h[1-6])>/gi, '\n');
    html = html.replace(/<\/li>/gi, '\n');
    html = html.replace(/<li[^>]*>/gi, '- ');
    // Remove all remaining tags
    html = html.replace(/<[^>]+>/g, '');
    // Decode HTML entities
    var el = document.createElement('textarea');
    el.innerHTML = html;
    html = el.value;
    // Normalize whitespace
    html = html.replace(/[ \t]+/g, ' ');
    html = html.replace(/\n /g, '\n');
    html = html.replace(/\n{3,}/g, '\n\n');
    return html.trim();
  }

  function getJobDescription() {
    // First, try JSON-LD structured data (most reliable)
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
              console.log('[EJS] Found description from JSON-LD @graph');
              return stripHtml(data['@graph'][g].description).substring(0, 10000);
            }
          }
        }

        if (data.description && data.description.length > 50) {
          console.log('[EJS] Found description from JSON-LD');
          return stripHtml(data.description).substring(0, 10000);
        }
      } catch (e) {
        console.log('[EJS] JSON-LD parse error: ' + e.message);
      }
    }

    // Fallback: try DOM selectors
    var descSelectors = [
      '.job-detail-description',
      '.job-description',
      '.job-description-content',
      '[class*="job-description"]',
      '[class*="description"]',
      '.ant-card-body',
      'article',
      'main [class*="content"]',
      '#job-description',
      'section[class*="description"]'
    ];

    for (var i = 0; i < descSelectors.length; i++) {
      try {
        var el = document.querySelector(descSelectors[i]);
        if (el && el.innerText && el.innerText.trim().length > 50) {
          console.log('[EJS] Found description using selector: ' + descSelectors[i]);
          return el.innerText.trim().substring(0, 10000);
        }
      } catch (e) {}
    }

    // Heuristic: find the largest text block in main content
    var mainContent = document.querySelector('main') ||
                      document.querySelector('[role="main"]') ||
                      document.querySelector('.main-content');

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
        console.log('[EJS] Found description using fallback (score: ' + bestScore + ')');
        return bestText.substring(0, 10000);
      }
    }

    return '';
  }

  function getJsonLdJobData() {
    var jsonLdScripts = document.querySelectorAll('script[type="application/ld+json"]');
    for (var k = 0; k < jsonLdScripts.length; k++) {
      try {
        var data = JSON.parse(jsonLdScripts[k].textContent);
        if (Array.isArray(data)) data = data[0];
        if (data['@type'] === 'JobPosting') return data;
        if (data['@graph'] && Array.isArray(data['@graph'])) {
          for (var g = 0; g < data['@graph'].length; g++) {
            if (data['@graph'][g]['@type'] === 'JobPosting') return data['@graph'][g];
          }
        }
      } catch (e) {}
    }
    return null;
  }

  function getCurrentJobId() {
    // URL pattern: /jobs/{category-slug}/{numeric-id}
    var match = window.location.href.match(/\/jobs\/[^\/]+\/(\d+)/);
    if (match) return match[1];

    // Fallback patterns
    var patterns = [/\/job\/(\d+)/, /\/jobs\/(\d+)/, /[?&]id=(\d+)/, /[?&]jobId=(\d+)/];
    for (var i = 0; i < patterns.length; i++) {
      var m = window.location.href.match(patterns[i]);
      if (m) return m[1];
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

  function getRecruiterInfo() {
    var contact = null;
    try {
      // JSON-LD: use existing getJsonLdJobData() for applicationContact and hiringOrganization.contactPoint
      var jsonLd = getJsonLdJobData();
      if (jsonLd) {
        if (jsonLd.applicationContact && jsonLd.applicationContact.name) {
          var ac = jsonLd.applicationContact;
          contact = { Name: ac.name };
          if (ac.email) contact.Email = ac.email;
          if (ac.telephone) contact.Phone = ac.telephone;
          if (ac.url) contact.ProfileUrl = ac.url;
        }
        if (!contact && jsonLd.hiringOrganization && jsonLd.hiringOrganization.contactPoint) {
          var cp = jsonLd.hiringOrganization.contactPoint;
          if (cp.name || cp.contactType) {
            contact = { Name: cp.name || cp.contactType };
            if (cp.email) contact.Email = cp.email;
            if (cp.telephone) contact.Phone = cp.telephone;
            if (cp.url) contact.ProfileUrl = cp.url;
          }
        }
      }

      // DOM fallback
      if (!contact) {
        var selectors = ['[class*="recruiter"]', '[class*="contact-person"]', '[class*="posted-by"]'];
        for (var i = 0; i < selectors.length; i++) {
          try {
            var el = document.querySelector(selectors[i]);
            if (!el) continue;
            var name = (el.textContent || '').trim().split('\n')[0].trim();
            if (name && name.length >= 2 && name.length < 100) {
              contact = { Name: name };
              var link = el.querySelector('a');
              if (link && link.href) contact.ProfileUrl = link.href.split('?')[0];
              break;
            }
          } catch (e) {}
        }
      }

      if (contact) {
        console.log('[EJS] Found recruiter: ' + contact.Name);
      }
    } catch (e) {
      console.log('[EJS] Error extracting recruiter info: ' + e.message);
    }
    return contact;
  }

  function updateDescription(url, description) {
    if (!url || !description || description.length < 50) {
      return Promise.resolve(false);
    }

    var normalizedUrl = normalizeJobUrl(url);
    console.log('[EJS] Updating description for URL:', normalizedUrl);

    var body = { Url: normalizedUrl, Description: description };
    var recruiter = getRecruiterInfo();
    if (recruiter) {
      body.Contacts = [recruiter];
    }

    return fetch(SERVER_URL + '/api/jobs/description', {
      method: 'PUT',
      headers: getHeaders(),
      body: JSON.stringify(body)
    })
    .then(function(r) { return r.json(); })
    .then(function(d) {
      if (d.updated) {
        console.log('[EJS] Description updated successfully');
        return true;
      }
      return false;
    })
    .catch(function(e) {
      console.log('[EJS] Error updating description: ' + e.message);
      return false;
    });
  }

  function extractJobIdFromUrl(url) {
    if (!url) return '';
    // /jobs/{category-slug}/{numeric-id}
    var match = url.match(/\/jobs\/[^\/]+\/(\d+)/);
    if (match) return match[1];

    var patterns = [/\/job\/([^\/\?]+)/, /\/jobs\/([^\/\?]+)/, /[?&]id=([^&]+)/];
    for (var i = 0; i < patterns.length; i++) {
      var m = url.match(patterns[i]);
      if (m) return m[1];
    }
    return '';
  }

  function findAllJobCards() {
    var cards = [];

    // EnergyJobSearch is a React SPA (Ant Design)
    // Try various selectors for job cards
    var cardSelectors = [
      'a[href*="/jobs/"]',
      '.ant-card',
      '.ant-list-item',
      '.job-card',
      '.job-listing',
      '[class*="job-card"]',
      '[class*="job-listing"]',
      'article[class*="job"]',
      '.job-item',
      'li[class*="job"]'
    ];

    // First try getting links to job detail pages
    var jobLinks = document.querySelectorAll('a[href*="/jobs/"]');
    var seenUrls = {};
    jobLinks.forEach(function(link) {
      var href = link.href || '';
      // Only match detail page links: /jobs/{slug}/{id}
      var jobId = extractJobIdFromUrl(href);
      if (!jobId || seenUrls[href]) return;
      seenUrls[href] = true;

      // Find the containing card element
      var container = link.closest('.ant-card') ||
                      link.closest('.ant-list-item') ||
                      link.closest('[class*="card"]') ||
                      link.closest('li') ||
                      link.closest('article') ||
                      link.closest('div');

      cards.push({ element: container || link, jobId: jobId, url: href, link: link });
    });

    if (cards.length > 0) {
      console.log('[EJS] Found ' + cards.length + ' job cards via links');
      return cards;
    }

    // Fallback: try card selectors
    for (var s = 0; s < cardSelectors.length; s++) {
      try {
        var foundCards = document.querySelectorAll(cardSelectors[s]);
        if (foundCards.length > 0) {
          foundCards.forEach(function(card) {
            var link = card.querySelector('a[href*="/jobs/"]') || card.querySelector('a');
            if (link && link.href) {
              var jobId = extractJobIdFromUrl(link.href);
              if (jobId) {
                cards.push({ element: card, jobId: jobId, url: link.href, link: link });
              }
            }
          });
          if (cards.length > 0) break;
        }
      } catch (e) {}
    }

    console.log('[EJS] Found ' + cards.length + ' job cards');
    return cards;
  }

  function extractJobFromCard(card) {
    var el = card.element;
    var url = normalizeJobUrl(card.url);

    var title = '';
    var titleSelectors = ['.job-title', 'h2', 'h3', 'h4', '[class*="title"]', 'strong', 'a[href*="/jobs/"]'];
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

    // If no title from selectors, try the link text itself
    if ((!title || title.length < 3) && card.link) {
      var linkText = cleanText(card.link.textContent);
      if (linkText && linkText.length > 3 && linkText.length < 200) {
        title = linkText;
      }
    }

    if (!title || title.length < 3) return null;

    var company = '';
    var companySelectors = ['.company-name', '.employer', '.company', '[class*="company"]', '[class*="employer"]', '[class*="organization"]'];
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
      Source: 'EnergyJobSearch'
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

    console.log('[EJS] Extracted ' + jobs.length + ' unique jobs');

    // On detail pages, try to get description from JSON-LD
    var currentDesc = getJobDescription();
    var currentUrl = getCurrentJobUrl();
    var jsonLd = getJsonLdJobData();

    // If on detail page with JSON-LD, enrich the data
    if (jsonLd) {
      var currentJobId = getCurrentJobId();
      if (currentJobId && !seenIds[currentJobId]) {
        var detailJob = {
          Title: jsonLd.title || '',
          Company: '',
          Location: '',
          Description: currentDesc || '',
          JobType: 0,
          Salary: '',
          Url: normalizeJobUrl(currentUrl),
          DatePosted: jsonLd.datePosted || new Date().toISOString(),
          IsRemote: false,
          Skills: [],
          Source: 'EnergyJobSearch'
        };

        if (jsonLd.hiringOrganization && jsonLd.hiringOrganization.name) {
          detailJob.Company = jsonLd.hiringOrganization.name;
        }

        if (jsonLd.jobLocation) {
          var loc = jsonLd.jobLocation;
          if (loc.address) {
            var parts = [];
            if (loc.address.addressLocality) parts.push(loc.address.addressLocality);
            if (loc.address.addressRegion) parts.push(loc.address.addressRegion);
            if (loc.address.addressCountry) parts.push(loc.address.addressCountry);
            detailJob.Location = parts.join(', ');
          }
        }

        var recruiter = getRecruiterInfo();
        if (recruiter) {
          detailJob.Contacts = [recruiter];
        }
        jobs.push(detailJob);
        seenIds[currentJobId] = true;
      }
    }

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

    // Also try to extract detail from DOM on detail page
    var currentJobId2 = getCurrentJobId();
    if (currentJobId2 && !seenIds[currentJobId2]) {
      var h1 = document.querySelector('h1');
      if (h1) {
        var detailTitle = cleanText(h1.textContent);
        if (detailTitle && detailTitle.length > 3) {
          var compEl = document.querySelector('.company-name, .employer, [class*="company"], [class*="organization"]');
          var locEl = document.querySelector('.location, [class*="location"]');
          var salEl = document.querySelector('.salary, [class*="salary"]');

          var domDetailJob = {
            Title: detailTitle,
            Company: compEl ? cleanText(compEl.textContent) : '',
            Location: locEl ? cleanText(locEl.textContent) : '',
            Description: currentDesc || '',
            JobType: 0,
            Salary: salEl ? cleanText(salEl.textContent) : '',
            Url: normalizeJobUrl(currentUrl),
            DatePosted: new Date().toISOString(),
            IsRemote: false,
            Skills: [],
            Source: 'EnergyJobSearch'
          };
          var domRecruiter = getRecruiterInfo();
          if (domRecruiter) {
            domDetailJob.Contacts = [domRecruiter];
          }
          jobs.push(domDetailJob);
        }
      }
    }

    // Filter out jobs already sent to server
    var newJobs = jobs.filter(function(j) { return !sentJobIds[j.Url]; });
    console.log('[EJS] Total jobs: ' + jobs.length + ', new: ' + newJobs.length);

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
          console.log('[EJS] Added: ' + job.Title.substring(0, 40));
        } else {
          totalDuplicates++;
          if (job.Description && job.Description.length > 50) {
            updateDescription(job.Url, job.Description);
          }
        }
        next();
      })
      .catch(function(e) {
        console.log('[EJS] Error: ' + e.message);
        next();
      });
    }

    next();
  }

  function checkAndFetchDescriptions() {
    if (fetchingDescriptions || sending) return;
    if (!window.location.href.includes('energyjobsearch.com')) return;

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
        console.log('[EJS] Error checking for missing descriptions: ' + e.message);
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
          return jobUrl.includes('energyjobsearch.com');
        }).map(function(job) {
          return {
            Url: normalizeJobUrl(job.Url || job.url || ''),
            Title: job.Title || job.title || 'Unknown Job',
            Company: job.Company || job.company || ''
          };
        });

        if (autoFetchQueue.length === 0) {
          alert('No EnergyJobSearch jobs need descriptions.');
          return;
        }

        autoFetchIndex = 0;
        autoFetchEnabled = true;

        sessionStorage.setItem('ejs-autofetch', JSON.stringify({
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
    sessionStorage.removeItem('ejs-autofetch');
    hideAutoFetchUI();
  }

  function checkAutoFetchState() {
    var state = sessionStorage.getItem('ejs-autofetch');
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
      sessionStorage.removeItem('ejs-autofetch');
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
          var state = JSON.parse(sessionStorage.getItem('ejs-autofetch') || '{}');
          state.index = autoFetchIndex;
          sessionStorage.setItem('ejs-autofetch', JSON.stringify(state));

          if (autoFetchIndex >= autoFetchQueue.length) {
            completeAutoFetch();
          } else {
            setTimeout(navigateToNextJob, autoFetchDelay);
          }
        });
      } else {
        autoFetchIndex++;
        var state = JSON.parse(sessionStorage.getItem('ejs-autofetch') || '{}');
        state.index = autoFetchIndex;
        sessionStorage.setItem('ejs-autofetch', JSON.stringify(state));

        if (autoFetchIndex >= autoFetchQueue.length) {
          completeAutoFetch();
        } else {
          setTimeout(navigateToNextJob, 1000);
        }
      }
    }, 4000); // Longer delay for React SPA to render
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
      console.log('[EJS] Crawl or auto-fetch already running');
      return;
    }
    crawlDelay = (delaySeconds || 5) * 1000;
    crawlMaxPages = maxPages || 20;
    crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
    crawlEnabled = true;

    sessionStorage.setItem('ejs-crawl', JSON.stringify({
      enabled: true,
      delay: crawlDelay,
      maxPages: crawlMaxPages,
      results: crawlResults
    }));

    console.log('[EJS] Starting crawl with ' + (crawlDelay/1000) + 's delay, max ' + crawlMaxPages + ' pages');
    showCrawlUI();
    processCrawlPage();
  }

  function stopCrawl() {
    crawlEnabled = false;
    sessionStorage.removeItem('ejs-crawl');
    hideCrawlUI();
    console.log('[EJS] Crawl stopped. Pages: ' + crawlResults.pagesScanned + ', Found: ' + crawlResults.jobsFound + ', Added: ' + crawlResults.jobsAdded);
  }

  function checkCrawlState() {
    var state = sessionStorage.getItem('ejs-crawl');
    if (!state) return;
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled) {
        crawlEnabled = true;
        crawlDelay = parsed.delay || 5000;
        crawlMaxPages = parsed.maxPages || 20;
        crawlResults = parsed.results || { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
        console.log('[EJS] Resuming crawl - page ' + (crawlResults.pagesScanned + 1));
        showCrawlUI();
        processCrawlPage();
      }
    } catch (e) {
      sessionStorage.removeItem('ejs-crawl');
    }
  }

  function processCrawlPage() {
    if (!crawlEnabled) return;
    if (crawlResults.pagesScanned >= crawlMaxPages) {
      completeCrawl();
      return;
    }

    updateCrawlUI();

    // Wait longer for React SPA content to load
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

      console.log('[EJS] Crawl page ' + (crawlResults.pagesScanned + 1) + ': found ' + pageJobs.length + ' jobs');
      crawlResults.jobsFound += pageJobs.length;
      crawlResults.pagesScanned++;

      if (pageJobs.length === 0) {
        console.log('[EJS] No jobs found on page, stopping crawl');
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
          console.log('[EJS] Crawl: navigating to next page in ' + (crawlDelay/1000) + 's');
          setTimeout(function() {
            if (crawlEnabled) window.location.href = nextUrl;
          }, crawlDelay);
        } else {
          console.log('[EJS] Crawl: no next page found');
          completeCrawl();
        }
      });
    }, 4000); // 4s for React SPA
  }

  function findNextPageUrl() {
    // Try next page link in pagination
    var nextLink = document.querySelector('a[rel="next"]') ||
                   document.querySelector('.ant-pagination-next:not(.ant-pagination-disabled) a') ||
                   document.querySelector('.pagination .next a') ||
                   document.querySelector('a[aria-label="Next"]') ||
                   document.querySelector('li.ant-pagination-next:not(.ant-pagination-disabled) a');
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
    sessionStorage.setItem('ejs-crawl', JSON.stringify({
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
    console.log('[EJS] ' + msg);
    alert(msg);
  }

  function showCrawlUI() {
    var existing = document.getElementById('ejs-crawl-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'ejs-crawl-ui';
    el.innerHTML = '<div id="ejs-cr-status">Crawling search results...</div>' +
                   '<div id="ejs-cr-progress">Page <span id="ejs-cr-page">0</span> | Found <span id="ejs-cr-found">0</span> | Added <span id="ejs-cr-added">0</span></div>' +
                   '<button id="ejs-cr-stop">Stop Crawl</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#057642;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#ejs-cr-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#ejs-cr-progress').style.cssText = 'margin-bottom:12px;';
    el.querySelector('#ejs-cr-stop').style.cssText = 'background:#dc3545;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#ejs-cr-stop').onclick = stopCrawl;
    document.body.appendChild(el);
  }

  function updateCrawlUI() {
    var p = document.getElementById('ejs-cr-page');
    var f = document.getElementById('ejs-cr-found');
    var a = document.getElementById('ejs-cr-added');
    if (p) p.textContent = crawlResults.pagesScanned;
    if (f) f.textContent = crawlResults.jobsFound;
    if (a) a.textContent = crawlResults.jobsAdded;
  }

  function hideCrawlUI() {
    var el = document.getElementById('ejs-crawl-ui');
    if (el) el.remove();
  }

  setTimeout(checkCrawlState, 3000);

  // === Availability Check (in-browser navigation) ===
  var availCheckEnabled = false;
  var availCheckQueue = [];
  var availCheckIndex = 0;
  var availResults = { checked: 0, markedUnavailable: 0, errors: 0, skipped: 0 };

  function startAvailabilityCheck() {
    if (availCheckEnabled || autoFetchEnabled || crawlEnabled) {
      console.log('[EJS] Another operation is already running');
      return;
    }

    fetch(SERVER_URL + '/api/jobs/for-availability-check?source=EnergyJobSearch', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          alert('No EnergyJobSearch jobs to check!');
          return;
        }

        availCheckQueue = data.jobs.map(function(job) {
          return {
            Id: job.Id || job.id,
            Url: job.Url || job.url || '',
            Title: job.Title || job.title || 'Unknown'
          };
        });
        availCheckIndex = 0;
        availResults = { checked: 0, markedUnavailable: 0, errors: 0, skipped: 0 };
        availCheckEnabled = true;

        sessionStorage.setItem('ejs-availcheck', JSON.stringify({
          enabled: true,
          queue: availCheckQueue,
          index: availCheckIndex,
          results: availResults
        }));

        showAvailCheckUI();
        navigateToNextAvailCheck();
      })
      .catch(function(e) {
        alert('Error: ' + e.message);
      });
  }

  function stopAvailabilityCheck() {
    availCheckEnabled = false;
    availCheckQueue = [];
    availCheckIndex = 0;
    sessionStorage.removeItem('ejs-availcheck');
    hideAvailCheckUI();
  }

  function checkAvailCheckState() {
    var state = sessionStorage.getItem('ejs-availcheck');
    if (!state) return;
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled) {
        availCheckEnabled = true;
        availCheckQueue = parsed.queue;
        availCheckIndex = parsed.index || 0;
        availResults = parsed.results || { checked: 0, markedUnavailable: 0, errors: 0, skipped: 0 };
        showAvailCheckUI();
        processAvailCheckPage();
      }
    } catch (e) {
      sessionStorage.removeItem('ejs-availcheck');
    }
  }

  function processAvailCheckPage() {
    if (!availCheckEnabled || availCheckIndex >= availCheckQueue.length) {
      completeAvailCheck();
      return;
    }

    var currentJob = availCheckQueue[availCheckIndex];
    updateAvailCheckUI();

    setTimeout(function() {
      var isUnavailable = false;
      var reason = '';

      // Check for unavailability indicators
      var pageText = document.body.innerText || '';
      var unavailableIndicators = [
        'This job is no longer available',
        'Job has expired',
        'No longer accepting applications',
        'This position has been filled'
        // Note: 'No longer available', 'Page not found', '404' excluded - React SPA bundles these strings in i18n messages
      ];

      for (var i = 0; i < unavailableIndicators.length; i++) {
        if (pageText.indexOf(unavailableIndicators[i]) !== -1) {
          isUnavailable = true;
          reason = unavailableIndicators[i];
          break;
        }
      }

      // Check JSON-LD for expiry
      if (!isUnavailable) {
        var jsonLd = getJsonLdJobData();
        if (jsonLd && jsonLd.validThrough) {
          var expiryDate = new Date(jsonLd.validThrough);
          if (expiryDate < new Date()) {
            isUnavailable = true;
            reason = 'Job expired on ' + expiryDate.toISOString().split('T')[0];
          }
        }
      }

      if (isUnavailable) {
        // Mark as unavailable on server
        var headers = getHeaders();
        fetch(SERVER_URL + '/api/jobs/' + currentJob.Id + '/mark-unsuitable', {
          method: 'POST',
          headers: headers,
          body: JSON.stringify({ reason: 'EnergyJobSearch: ' + reason })
        })
        .then(function() {
          availResults.markedUnavailable++;
          advanceAvailCheck();
        })
        .catch(function() {
          availResults.errors++;
          advanceAvailCheck();
        });
      } else {
        // Mark as checked
        fetch(SERVER_URL + '/api/jobs/' + currentJob.Id + '/mark-checked', {
          method: 'POST',
          headers: getHeaders()
        })
        .then(function() { advanceAvailCheck(); })
        .catch(function() {
          availResults.errors++;
          advanceAvailCheck();
        });
      }
    }, 4000);
  }

  function advanceAvailCheck() {
    availResults.checked++;
    availCheckIndex++;
    var state = JSON.parse(sessionStorage.getItem('ejs-availcheck') || '{}');
    state.index = availCheckIndex;
    state.results = availResults;
    sessionStorage.setItem('ejs-availcheck', JSON.stringify(state));
    updateAvailCheckUI();

    if (availCheckIndex >= availCheckQueue.length) {
      completeAvailCheck();
    } else {
      setTimeout(navigateToNextAvailCheck, 3000);
    }
  }

  function navigateToNextAvailCheck() {
    if (!availCheckEnabled || availCheckIndex >= availCheckQueue.length) {
      completeAvailCheck();
      return;
    }
    var job = availCheckQueue[availCheckIndex];
    if (job && job.Url) {
      window.location.href = job.Url;
    } else {
      availResults.skipped++;
      availCheckIndex++;
      setTimeout(navigateToNextAvailCheck, 500);
    }
  }

  function completeAvailCheck() {
    var results = availResults;
    stopAvailabilityCheck();
    alert('Availability check complete!\nChecked: ' + results.checked + '\nMarked unavailable: ' + results.markedUnavailable + '\nErrors: ' + results.errors);
  }

  function showAvailCheckUI() {
    var existing = document.getElementById('ejs-avail-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'ejs-avail-ui';
    el.innerHTML = '<div id="ejs-av-status">Checking job availability...</div>' +
                   '<div id="ejs-av-progress">Job <span id="ejs-av-current">0</span> of <span id="ejs-av-total">0</span></div>' +
                   '<div id="ejs-av-results">Unavailable: <span id="ejs-av-unavail">0</span></div>' +
                   '<button id="ejs-av-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#dc3545;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#ejs-av-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#ejs-av-progress').style.cssText = 'margin-bottom:4px;';
    el.querySelector('#ejs-av-results').style.cssText = 'margin-bottom:12px;font-size:11px;';
    el.querySelector('#ejs-av-stop').style.cssText = 'background:#a71d2a;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#ejs-av-stop').onclick = stopAvailabilityCheck;
    document.body.appendChild(el);
  }

  function updateAvailCheckUI() {
    var c = document.getElementById('ejs-av-current');
    var t = document.getElementById('ejs-av-total');
    var u = document.getElementById('ejs-av-unavail');
    if (c) c.textContent = availCheckIndex + 1;
    if (t) t.textContent = availCheckQueue.length;
    if (u) u.textContent = availResults.markedUnavailable;
  }

  function hideAvailCheckUI() {
    var el = document.getElementById('ejs-avail-ui');
    if (el) el.remove();
  }

  setTimeout(checkAvailCheckState, 2500);

  window.EJS = {
    extract: doExtract,
    status: function() { return { sent: totalSent, dup: totalDuplicates }; },
    test: function() {
      fetch(SERVER_URL + '/api/jobs', { headers: getHeaders() })
        .then(function(r) { return r.json(); })
        .then(function(d) { console.log('[EJS] Server has ' + d.length + ' jobs'); })
        .catch(function(e) { console.log('[EJS] Error: ' + e.message); });
    },
    getDesc: function() {
      var desc = getJobDescription();
      console.log('[EJS] Description (' + desc.length + ' chars):', desc.substring(0, 300));
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
    checkAvailability: startAvailabilityCheck,
    stopAvailability: stopAvailabilityCheck,
    debug: function() {
      console.log('[EJS] === DEBUG ===');
      console.log('[EJS] URL:', window.location.href);
      console.log('[EJS] Server URL:', SERVER_URL);
      console.log('[EJS] API Key:', API_KEY ? '(set)' : '(not set)');
      console.log('[EJS] Current Job ID:', getCurrentJobId());
      console.log('[EJS] Description length:', getJobDescription().length);
      var jsonLd = getJsonLdJobData();
      console.log('[EJS] JSON-LD:', jsonLd ? 'Found' : 'Not found');
      if (jsonLd) {
        console.log('[EJS] JSON-LD title:', jsonLd.title);
        console.log('[EJS] JSON-LD validThrough:', jsonLd.validThrough);
      }
    },
    help: function() {
      console.log('[EJS] === COMMANDS ===');
      console.log('EJS.extract()             - Extract jobs from current page');
      console.log('EJS.debug()               - Show debug info');
      console.log('EJS.test()                - Test server connection');
      console.log('EJS.autoFetch(3)          - Start auto-fetching descriptions');
      console.log('EJS.stopAutoFetch()       - Stop auto-fetching');
      console.log('');
      console.log('=== CRAWL ===');
      console.log('EJS.crawl(5, 20)          - Crawl search results (5s delay, 20 pages max)');
      console.log('EJS.stopCrawl()           - Stop crawling');
      console.log('EJS.crawlStatus()         - Check crawl progress');
      console.log('');
      console.log('=== AVAILABILITY ===');
      console.log('EJS.checkAvailability()   - Check if jobs are still available');
      console.log('EJS.stopAvailability()    - Stop availability check');
    }
  };

  console.log('[EJS] Ready! Type EJS.help() for commands.');
})();
