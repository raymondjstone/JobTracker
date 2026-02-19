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

  console.log('[WTTJ] Welcome to the Jungle Job Extractor loaded');

  // Load server URL and API key from storage
  if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
      if (result.serverUrl) {
        SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        console.log('[WTTJ] Using server URL from settings:', SERVER_URL);
      }
      if (result.apiKey) {
        API_KEY = result.apiKey;
        console.log('[WTTJ] API key loaded');
      }
    });
    // Listen for changes from popup
    chrome.storage.onChanged.addListener(function(changes) {
      if (changes.serverUrl) {
        SERVER_URL = (changes.serverUrl.newValue || SERVER_URL).replace(/\/+$/, '');
      }
      if (changes.apiKey) {
        API_KEY = changes.apiKey.newValue || '';
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
  window.WTTJExtractorInstalled = true;
  document.documentElement.setAttribute('data-wttj-installed', 'true');

  createUI();

  // Wait for content to be ready, with multiple retries
  function waitForContentAndExtract(attempt) {
    attempt = attempt || 1;
    var maxAttempts = 5;

    // Check if there are any job links on the page
    var hasJobLinks = document.querySelectorAll('a[href*="/jobs/"]').length > 0 ||
                      document.querySelectorAll('a[href*="/companies/"][href*="/jobs/"]').length > 0;

    if (hasJobLinks) {
      console.log('[WTTJ] Content ready, extracting (attempt ' + attempt + ')');
      doExtract();
    } else if (attempt < maxAttempts) {
      console.log('[WTTJ] Waiting for content... (attempt ' + attempt + '/' + maxAttempts + ')');
      setTimeout(function() { waitForContentAndExtract(attempt + 1); }, 2000);
    } else {
      console.log('[WTTJ] No job content found after ' + maxAttempts + ' attempts');
      doExtract(); // Try anyway
    }
  }

  setTimeout(waitForContentAndExtract, 2000); // Initial delay for React to start rendering

  window.addEventListener('scroll', function() {
    setTimeout(doExtract, 1500);
  });

  setInterval(doExtract, 8000);
  setInterval(checkAndFetchDescriptions, 30000);
  setTimeout(checkAndFetchDescriptions, 5000);
  setTimeout(checkAutoFetchState, 1500);

  // Listen for URL changes (SPA navigation)
  var lastUrl = location.href;
  new MutationObserver(function() {
    var url = location.href;
    if (url !== lastUrl) {
      lastUrl = url;
      console.log('[WTTJ] URL changed, re-extracting...');
      setTimeout(doExtract, 2000);
    }
  }).observe(document, { subtree: true, childList: true });

  function createUI() {
    if (document.getElementById('wttj-ui')) return;
    var el = document.createElement('div');
    el.id = 'wttj-ui';
    el.innerHTML = '<span id="wttj-dot"></span><span id="wttj-text">Ready</span><span id="wttj-count">0</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#FFCD00;color:#000;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#wttj-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#wttj-count').style.cssText = 'background:rgba(0,0,0,0.2);padding:2px 8px;border-radius:10px;';
    document.body.appendChild(el);
    el.onclick = doExtract;
  }

  function updateUI(text, count) {
    var t = document.getElementById('wttj-text');
    var c = document.getElementById('wttj-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }

  function showAutoFetchUI() {
    var existing = document.getElementById('wttj-autofetch-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'wttj-autofetch-ui';
    el.innerHTML = '<div id="wttj-af-status">Auto-fetching descriptions...</div>' +
                   '<div id="wttj-af-progress">Job <span id="wttj-af-current">0</span> of <span id="wttj-af-total">0</span></div>' +
                   '<div id="wttj-af-job"></div>' +
                   '<button id="wttj-af-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#FFCD00;color:#000;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#wttj-af-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#wttj-af-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#wttj-af-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#wttj-af-stop').style.cssText = 'background:#000;border:none;color:#FFCD00;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#wttj-af-stop').onclick = stopAutoFetch;
    document.body.appendChild(el);
  }

  function updateAutoFetchUI(current, total, jobTitle) {
    var c = document.getElementById('wttj-af-current');
    var t = document.getElementById('wttj-af-total');
    var j = document.getElementById('wttj-af-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }

  function hideAutoFetchUI() {
    var el = document.getElementById('wttj-autofetch-ui');
    if (el) el.remove();
  }

  function cleanText(text) {
    if (!text) return '';
    text = text.trim().replace(/\s+/g, ' ').replace(/\n/g, ' ');
    return text.trim();
  }

  function getJobDescription() {
    var descParts = [];

    // Get "Who you are" requirements
    var requirements = document.querySelectorAll('[data-testid="job-requirement-bullet"]');
    if (requirements.length > 0) {
      descParts.push('Who you are:');
      requirements.forEach(function(el) {
        descParts.push('• ' + cleanText(el.textContent));
      });
    }

    // Get "What the job involves"
    var involves = document.querySelectorAll('[data-testid="job-involves-bullet"]');
    if (involves.length > 0) {
      descParts.push('\nWhat the job involves:');
      involves.forEach(function(el) {
        descParts.push('• ' + cleanText(el.textContent));
      });
    }

    // Get company benefits
    var benefits = document.querySelectorAll('[data-testid="company-benefit-bullet"]');
    if (benefits.length > 0) {
      descParts.push('\nCompany benefits:');
      benefits.forEach(function(el) {
        descParts.push('• ' + cleanText(el.textContent));
      });
    }

    // Get company mission
    var mission = document.querySelector('[data-testid="company-mission"]');
    if (mission) {
      descParts.push('\nCompany mission: ' + cleanText(mission.textContent));
    }

    // Get "Our take" / market analysis
    var marketBullets = document.querySelectorAll('[data-testid="company-market-bullet"]');
    if (marketBullets.length > 0) {
      descParts.push('\nAbout the company:');
      marketBullets.forEach(function(el) {
        descParts.push(cleanText(el.textContent));
      });
    }

    if (descParts.length > 0) {
      console.log('[WTTJ] Found description using data-testid selectors');
      return descParts.join('\n').substring(0, 10000);
    }

    // Fallback: get main job card content
    var jobCard = document.querySelector('[data-testid="job-card-main"]');
    if (jobCard && jobCard.innerText && jobCard.innerText.trim().length > 100) {
      console.log('[WTTJ] Found description using job-card-main fallback');
      return jobCard.innerText.trim().substring(0, 10000);
    }

    // Fallback 2: Try JSON-LD structured data (like S1Jobs)
    var jsonLdScripts = document.querySelectorAll('script[type="application/ld+json"]');
    for (var i = 0; i < jsonLdScripts.length; i++) {
      try {
        var data = JSON.parse(jsonLdScripts[i].textContent);
        // Handle array or single object
        var items = Array.isArray(data) ? data : [data];
        for (var j = 0; j < items.length; j++) {
          var item = items[j];
          if (item['@type'] === 'JobPosting' && item.description) {
            var desc = item.description;
            // Strip HTML tags if present
            desc = desc.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim();
            if (desc.length > 100) {
              console.log('[WTTJ] Found description using JSON-LD structured data');
              return desc.substring(0, 10000);
            }
          }
        }
      } catch (e) {
        // Invalid JSON, continue
      }
    }

    // Fallback 3: Generic class patterns
    var genericSelectors = [
      '[class*="Description"]',
      '[class*="description"]',
      '[class*="JobDescription"]',
      '[class*="job-description"]',
      '[class*="jobDescription"]',
      'article [class*="content"]',
      'main [class*="content"]'
    ];

    for (var k = 0; k < genericSelectors.length; k++) {
      var elements = document.querySelectorAll(genericSelectors[k]);
      for (var l = 0; l < elements.length; l++) {
        var el = elements[l];
        var text = el.innerText || '';
        // Validate: substantial text with multiple paragraphs (3+ newlines)
        if (text.length >= 200 && text.length <= 15000 && (text.match(/\n/g) || []).length >= 3) {
          console.log('[WTTJ] Found description using generic class selector: ' + genericSelectors[k]);
          return text.trim().substring(0, 10000);
        }
      }
    }

    // Fallback 4: Article or main content area
    var contentAreas = document.querySelectorAll('article, main section, main > div');
    for (var m = 0; m < contentAreas.length; m++) {
      var area = contentAreas[m];
      var areaText = area.innerText || '';
      // Look for substantial text blocks with paragraph structure
      if (areaText.length >= 200 && areaText.length <= 15000) {
        var newlineCount = (areaText.match(/\n/g) || []).length;
        // Must have some paragraph structure (3+ newlines)
        if (newlineCount >= 3) {
          // Avoid navigation-heavy areas (too many short lines)
          var lines = areaText.split('\n').filter(function(line) { return line.trim().length > 0; });
          var avgLineLength = areaText.length / lines.length;
          // Good content has average line length > 30 chars
          if (avgLineLength > 30) {
            console.log('[WTTJ] Found description using main content area fallback');
            return areaText.trim().substring(0, 10000);
          }
        }
      }
    }

    // Fallback 5: Find the largest text block on the page that looks like job content
    var allDivs = document.querySelectorAll('div, section');
    var bestCandidate = null;
    var bestScore = 0;

    for (var n = 0; n < allDivs.length; n++) {
      var div = allDivs[n];
      var divText = div.innerText || '';

      // Skip if too short or too long
      if (divText.length < 200 || divText.length > 15000) continue;

      // Skip if it's a child of an already-found parent
      if (bestCandidate && bestCandidate.contains(div)) continue;

      // Calculate score based on text characteristics
      var paragraphs = (divText.match(/\n\n/g) || []).length;
      var bullets = (divText.match(/[•\-\*]\s/g) || []).length;
      var hasJobKeywords = /responsibilities|requirements|qualifications|experience|skills|about\s+(the\s+)?(role|job|position)/i.test(divText);

      var score = divText.length * 0.1 + paragraphs * 50 + bullets * 20 + (hasJobKeywords ? 500 : 0);

      if (score > bestScore) {
        bestScore = score;
        bestCandidate = div;
      }
    }

    if (bestCandidate && bestScore > 200) {
      console.log('[WTTJ] Found description using content analysis fallback (score: ' + bestScore + ')');
      return bestCandidate.innerText.trim().substring(0, 10000);
    }

    return '';
  }

  function getCurrentJobId() {
    // WTTJ/Otta URL patterns: /jobs/[jobId] or /jobs/[jobId]/company
    var patterns = [
      /\/jobs\/([a-zA-Z0-9_-]+)(?:\/|$|\?)/
    ];

    for (var i = 0; i < patterns.length; i++) {
      var match = window.location.href.match(patterns[i]);
      if (match) return match[1];
    }

    // Try to get from navigation links
    var navLink = document.querySelector('[data-testid="company-nav"] a[href*="/jobs/"]');
    if (navLink) {
      var hrefMatch = navLink.href.match(/\/jobs\/([a-zA-Z0-9_-]+)/);
      if (hrefMatch) return hrefMatch[1];
    }

    return '';
  }

  function getCurrentJobUrl() {
    // Clean URL, removing extra query params
    return window.location.href.split('?')[0];
  }

  function normalizeJobUrl(url) {
    if (!url) return '';
    // Remove query string and trailing slash
    return url.split('?')[0].replace(/\/$/, '').toLowerCase();
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
          console.log('[WTTJ] Error updating description - HTTP ' + r.status + ': ' + text.substring(0, 100));
          return { updated: false };
        });
      }
      return r.json();
    })
    .then(function(d) {
      if (d.updated) {
        console.log('[WTTJ] Description updated');
        return true;
      }
      return false;
    })
    .catch(function(e) {
      console.log('[WTTJ] Error updating description: ' + e.message);
      return false;
    });
  }

  function findAllJobCards() {
    var cards = [];

    // WTTJ/Otta job card selectors - try multiple patterns
    var cardSelectors = [
      '[data-testid="job-card-v2"]',
      '[data-testid="job-card"]',
      '[data-testid="search-results-list-item"]',
      '[data-testid="search-results-list-item-wrapper"]',
      // WTTJ search results page selectors
      'li[data-testid]',
      'article[class*="Job"]',
      'div[class*="SearchResult"]',
      'div[class*="JobCard"]',
      // WTTJ main jobs page - look for list items containing job links
      'ul li a[href*="/companies/"][href*="/jobs/"]',
      'ol li a[href*="/companies/"][href*="/jobs/"]',
      // Role-based selectors
      '[role="listitem"] a[href*="/jobs/"]',
      '[role="article"] a[href*="/jobs/"]',
      // Generic link patterns for WTTJ
      'a[href*="/en/companies/"][href*="/jobs/"]',
      'a[href*="/fr/companies/"][href*="/jobs/"]',
      // Generic fallback - find all links to jobs
      'a[href*="/jobs/"][href*="welcometothejungle"]',
      'a[href*="/companies/"][href*="/jobs/"]'
    ];

    for (var s = 0; s < cardSelectors.length; s++) {
      try {
        var foundCards = document.querySelectorAll(cardSelectors[s]);
        if (foundCards.length > 0) {
          console.log('[WTTJ] Found ' + foundCards.length + ' elements using selector: ' + cardSelectors[s]);
          foundCards.forEach(function(card) {
            // If we matched a link directly, get its parent as the card
            if (card.tagName === 'A') {
              var link = card;
              // Find a reasonable parent container (go up a few levels)
              card = link.closest('li') || link.closest('article') || link.closest('div[class*="Card"]') || link.parentElement.parentElement || link.parentElement;
            }

            // Find the job link from within card
            var link = card.querySelector('a[href*="/jobs/"]') ||
                       card.querySelector('a[href*="/companies/"][href*="/jobs/"]');

            // If card is itself a link
            if (!link && card.tagName === 'A' && card.href && card.href.includes('/jobs/')) {
              link = card;
            }

            if (!link) return;

            var href = link.href;
            var jobId = '';
            // Match patterns like /jobs/job-slug or /companies/company/jobs/job-slug
            var match = href.match(/\/jobs\/([a-zA-Z0-9_-]+)/);
            if (match) jobId = match[1];

            if (jobId && href) {
              cards.push({
                element: card,
                jobId: jobId,
                url: href.split('?')[0]
              });
            }
          });
          if (cards.length > 0) break;
        }
      } catch (e) {
        console.log('[WTTJ] Selector error:', e);
      }
    }

    // Fallback: Find all job links on page and create cards from them
    if (cards.length === 0) {
      console.log('[WTTJ] Trying fallback: finding all job links');
      // Try multiple link patterns for WTTJ
      var linkSelectors = [
        'a[href*="/companies/"][href*="/jobs/"]',
        'a[href*="/jobs/"][href$=".html"]',
        'a[href*="/jobs/"]:not([href$="/jobs/"]):not([href*="/jobs?"])'
      ];

      var allJobLinks = [];
      linkSelectors.forEach(function(sel) {
        var links = document.querySelectorAll(sel);
        links.forEach(function(l) { allJobLinks.push(l); });
      });

      // Dedupe links by href
      var seenHrefs = {};
      allJobLinks = allJobLinks.filter(function(l) {
        if (seenHrefs[l.href]) return false;
        seenHrefs[l.href] = true;
        return true;
      });

      console.log('[WTTJ] Found ' + allJobLinks.length + ' unique job links');

      allJobLinks.forEach(function(link) {
        var href = link.href;
        // Skip navigation links, only get actual job links
        if (href.includes('/jobs?') || href.endsWith('/jobs') || href.endsWith('/jobs/')) return;
        // Skip if it's just the base jobs URL
        if (href.match(/\/jobs\/?$/)) return;

        // Extract job ID from URL - handle both /jobs/slug and /companies/X/jobs/slug patterns
        var match = href.match(/\/jobs\/([a-zA-Z0-9_-]+)(?:$|\/|\?)/);
        if (match) {
          var jobId = match[1];
          // Find a card container by going up the DOM
          var cardEl = link.closest('li') || link.closest('article') ||
                       link.closest('[role="listitem"]') || link.closest('[role="article"]') ||
                       link.closest('[class*="Card"]') || link.closest('[class*="Result"]') ||
                       link.closest('[class*="Item"]') || link.closest('div[class]') ||
                       link.parentElement.parentElement || link.parentElement;

          cards.push({
            element: cardEl,
            jobId: jobId,
            url: href.split('?')[0]
          });
        }
      });
      if (cards.length > 0) {
        console.log('[WTTJ] Found ' + cards.length + ' jobs via link fallback');
      }
    }

    // If no cards found but we're on a job detail page, create a card from current page
    if (cards.length === 0) {
      var currentJobId = getCurrentJobId();
      if (currentJobId) {
        var jobCard = document.querySelector('[data-testid="job-card-v2"]') || document.body;
        cards.push({
          element: jobCard,
          jobId: currentJobId,
          url: window.location.origin + '/jobs/' + currentJobId
        });
        console.log('[WTTJ] Created card from current job detail page: ' + currentJobId);
      }
    }

    // Deduplicate by jobId
    var seen = {};
    cards = cards.filter(function(card) {
      if (seen[card.jobId]) return false;
      seen[card.jobId] = true;
      return true;
    });

    console.log('[WTTJ] Found ' + cards.length + ' unique job cards');
    return cards;
  }

  function extractJobFromCard(card) {
    var el = card.element;
    var url = card.url;

    // Find title using multiple strategies
    var title = '';
    var titleEl = el.querySelector('[data-testid="job-title"]') ||
                  el.querySelector('h1') ||
                  el.querySelector('h2') ||
                  el.querySelector('h3') ||
                  el.querySelector('[class*="Title"]') ||
                  el.querySelector('[class*="title"]') ||
                  document.querySelector('[data-testid="job-title"]');

    if (titleEl) {
      title = cleanText(titleEl.textContent);
      // Remove company name if it's appended (e.g., "Senior Backend Software Engineer, Pigment")
      var companyLink = titleEl.querySelector('a');
      if (companyLink) {
        title = title.replace(', ' + cleanText(companyLink.textContent), '').trim();
      }
    }

    // Fallback: get title from link text
    if (!title || title.length < 3) {
      var jobLink = el.querySelector('a[href*="/jobs/"]');
      if (jobLink) {
        // Get direct text content, not nested elements
        var linkText = '';
        jobLink.childNodes.forEach(function(node) {
          if (node.nodeType === Node.TEXT_NODE) {
            linkText += node.textContent;
          } else if (node.tagName === 'SPAN' || node.tagName === 'DIV') {
            linkText += node.textContent;
          }
        });
        linkText = cleanText(linkText || jobLink.textContent);
        if (linkText && linkText.length > 3 && linkText.length < 200) {
          title = linkText;
        }
      }
    }

    if (!title || title.length < 3) {
      return null;
    }

    // Find company from multiple sources
    var company = '';
    var companyLogoImg = el.querySelector('[data-testid="company-logo"] img') ||
                         el.querySelector('img[alt]') ||
                         document.querySelector('[data-testid="company-logo"] img');
    if (companyLogoImg && companyLogoImg.alt && companyLogoImg.alt.length > 1 && companyLogoImg.alt.length < 100) {
      company = cleanText(companyLogoImg.alt);
    }

    // Fallback: get from company link
    if (!company) {
      var companyEl = el.querySelector('[data-testid="company-name"]') ||
                      el.querySelector('[class*="Company"]') ||
                      el.querySelector('[class*="company"]') ||
                      el.querySelector('a[href*="/companies/"]');
      if (companyEl) {
        company = cleanText(companyEl.textContent);
      }
    }

    // Fallback: extract company from URL pattern /companies/company-name/jobs/
    if (!company && url) {
      var companyMatch = url.match(/\/companies\/([a-zA-Z0-9_-]+)\/jobs\//);
      if (companyMatch) {
        // Convert slug to readable name (e.g., "my-company" -> "My Company")
        company = companyMatch[1].replace(/-/g, ' ').replace(/\b\w/g, function(l) { return l.toUpperCase(); });
      }
    }

    // Fallback: get from any span/div that might contain company name
    if (!company) {
      var spans = el.querySelectorAll('span, div');
      for (var i = 0; i < spans.length && !company; i++) {
        var text = cleanText(spans[i].textContent);
        // Company names are typically short and not the title
        if (text && text.length > 2 && text.length < 60 && text !== title &&
            !text.includes('Remote') && !text.includes('Apply') && !text.includes('Save')) {
          // Check if this looks like a company name (no long sentences)
          if (text.split(' ').length < 6) {
            company = text;
          }
        }
      }
    }

    // Find locations from multiple sources
    var location = '';
    var locationTags = el.querySelectorAll('[data-testid="job-location-tag"]');
    if (locationTags.length === 0) {
      locationTags = el.querySelectorAll('[class*="location"], [class*="Location"]');
    }
    if (locationTags.length === 0) {
      locationTags = document.querySelectorAll('[data-testid="job-location-tag"]');
    }
    if (locationTags.length > 0) {
      var locs = [];
      locationTags.forEach(function(tag) {
        var locText = cleanText(tag.textContent);
        if (locText && !locs.includes(locText)) {
          locs.push(locText);
        }
      });
      location = locs.join(', ');
    }

    // Fallback: search for location-like text in card
    if (!location) {
      var allText = el.textContent || '';
      // Look for common location patterns
      var locationPatterns = [
        /(?:London|Manchester|Edinburgh|Glasgow|Birmingham|Bristol|Leeds|Liverpool|Newcastle|Sheffield|Cambridge|Oxford|Remote|Hybrid|UK|United Kingdom)/gi
      ];
      locationPatterns.forEach(function(pattern) {
        var matches = allText.match(pattern);
        if (matches && matches.length > 0) {
          location = matches.slice(0, 3).join(', ');
        }
      });
    }

    // Check for remote
    var isRemote = location.toLowerCase().includes('remote') ||
                   (el.textContent && el.textContent.toLowerCase().includes('remote'));

    // Find salary from multiple sources
    var salary = '';
    var salarySection = el.querySelector('[data-testid="salary-section"]') ||
                        el.querySelector('[class*="salary"], [class*="Salary"]') ||
                        document.querySelector('[data-testid="salary-section"]');
    if (salarySection) {
      salary = cleanText(salarySection.textContent);
    }

    // Fallback: look for salary patterns in text
    if (!salary) {
      var cardText = el.textContent || '';
      var salaryMatch = cardText.match(/[£$€]\s*[\d,]+(?:\s*-\s*[£$€]?\s*[\d,]+)?(?:\s*(?:k|K|per\s+(?:year|annum|month)))?/);
      if (salaryMatch) {
        salary = cleanText(salaryMatch[0]);
      }
    }

    // Find experience level
    var experienceSection = el.querySelector('[data-testid="experience-section"]') ||
                            document.querySelector('[data-testid="experience-section"]');
    var experienceLevel = experienceSection ? cleanText(experienceSection.textContent) : '';

    // Find technologies/skills
    var skills = [];
    var techTags = el.querySelectorAll('[data-testid="job-technology-used"] > div');
    if (techTags.length === 0) {
      techTags = el.querySelectorAll('[class*="tag"], [class*="Tag"], [class*="skill"], [class*="Skill"]');
    }
    if (techTags.length === 0) {
      techTags = document.querySelectorAll('[data-testid="job-technology-used"] > div');
    }
    techTags.forEach(function(tag) {
      var skillText = cleanText(tag.textContent);
      if (skillText && skillText.length < 50 && skills.length < 15 && !skills.includes(skillText)) {
        skills.push(skillText);
      }
    });

    // Get company short description
    var companyDesc = '';
    var companyDescEl = el.querySelector('[data-testid="company-short-description"]') ||
                        document.querySelector('[data-testid="company-short-description"]');
    if (companyDescEl) {
      companyDesc = cleanText(companyDescEl.textContent);
    }

    return {
      Title: title,
      Company: company,
      Location: location,
      Description: companyDesc ? ('Company: ' + companyDesc + (experienceLevel ? '\nLevel: ' + experienceLevel : '')) : '',
      JobType: 0,
      Salary: salary,
      Url: url,
      DatePosted: new Date().toISOString(),
      IsRemote: isRemote,
      Skills: skills,
      Source: 'WTTJ'
    };
  }

  function doExtract() {
    if (autoFetchEnabled) {
      console.log('[WTTJ] Skipping extraction - auto-fetch is running');
      return;
    }
    if (crawlEnabled) {
      console.log('[WTTJ] Skipping extraction - crawl is running');
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

    console.log('[WTTJ] Extracted ' + jobs.length + ' unique jobs');

    // Get description for currently viewed job (if on detail page)
    var currentDesc = getJobDescription();
    var currentUrl = getCurrentJobUrl();
    var currentJobId = getCurrentJobId();

    if (currentDesc && currentDesc.length > 50 && currentUrl) {
      console.log('[WTTJ] Found description (' + currentDesc.length + ' chars) for current job');

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
    if (currentJobId && !seenIds[currentJobId]) {
      var titleEl = document.querySelector('[data-testid="job-title"]') || document.querySelector('h1');
      if (titleEl) {
        var detailTitle = cleanText(titleEl.textContent);
        // Remove company name if appended
        var companyLink = titleEl.querySelector('a');
        if (companyLink) {
          detailTitle = detailTitle.replace(', ' + cleanText(companyLink.textContent), '').trim();
        }

        if (detailTitle && detailTitle.length > 3) {
          // Get company from logo
          var companyLogoImg = document.querySelector('[data-testid="company-logo"] img');
          var company = companyLogoImg ? companyLogoImg.alt : '';

          // Get locations
          var locationTags = document.querySelectorAll('[data-testid="job-location-tag"]');
          var locs = [];
          locationTags.forEach(function(tag) {
            locs.push(cleanText(tag.textContent));
          });
          var location = locs.join(', ');

          // Get salary
          var salarySection = document.querySelector('[data-testid="salary-section"]');
          var salary = salarySection ? cleanText(salarySection.textContent) : '';

          // Get skills
          var skills = [];
          var techTags = document.querySelectorAll('[data-testid="job-technology-used"] > div');
          techTags.forEach(function(tag) {
            var skillText = cleanText(tag.textContent);
            if (skillText && skills.length < 15) skills.push(skillText);
          });

          jobs.push({
            Title: detailTitle,
            Company: company,
            Location: location,
            Description: currentDesc || '',
            JobType: 0,
            Salary: salary,
            Url: currentUrl,
            DatePosted: new Date().toISOString(),
            IsRemote: location.toLowerCase().includes('remote'),
            Skills: skills,
            Source: 'WTTJ'
          });
          console.log('[WTTJ] Added detail view job: ' + detailTitle.substring(0, 30));
        }
      }
    }

    // Filter out jobs already sent to server
    var newJobs = jobs.filter(function(j) { return !sentJobIds[j.Url]; });
    console.log('[WTTJ] Total jobs: ' + jobs.length + ', new: ' + newJobs.length);

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

      console.log('[WTTJ] Sending job:', {
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
            console.log('[WTTJ] Error response:', text.substring(0, 200));
            throw new Error('HTTP ' + r.status + ': ' + text.substring(0, 100));
          });
        }
        return r.json();
      })
      .then(function(d) {
        sentJobIds[job.Url] = true;
        if (d.added === true) {
          totalSent++;
          console.log('[WTTJ] Added: ' + job.Title.substring(0, 40));
        } else {
          totalDuplicates++;
          if (job.Description && job.Description.length > 50) {
            updateDescription(job.Url, job.Description);
          }
        }
        next();
      })
      .catch(function(e) {
        console.log('[WTTJ] Error: ' + e.message);
        next();
      });
    }

    next();
  }

  function checkAndFetchDescriptions() {
    if (fetchingDescriptions || sending) return;
    if (!window.location.href.includes('welcometothejungle.com')) return;

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

        console.log('[WTTJ] Found ' + data.count + ' jobs needing descriptions');

        var currentUrl = normalizeJobUrl(getCurrentJobUrl());

        if (currentUrl) {
          var currentJobNeedsDesc = data.jobs.some(function(j) {
            return normalizeJobUrl(j.Url) === currentUrl;
          });

          if (currentJobNeedsDesc) {
            var desc = getJobDescription();
            if (desc && desc.length > 50) {
              console.log('[WTTJ] Current job needs description - sending it now');
              updateDescription(currentUrl, desc);
            }
          }
        }

        fetchingDescriptions = false;
      })
      .catch(function(e) {
        console.log('[WTTJ] Error checking for missing descriptions: ' + e.message);
        fetchingDescriptions = false;
      });
  }

  function startAutoFetch(delaySeconds) {
    if (autoFetchEnabled) {
      console.log('[WTTJ] Auto-fetch already running');
      return;
    }

    autoFetchDelay = (delaySeconds || 3) * 1000;
    console.log('[WTTJ] Starting auto-fetch with ' + (autoFetchDelay/1000) + 's delay');

    fetch(SERVER_URL + '/api/jobs/needing-descriptions', {
      headers: getHeaders()
    })
      .then(function(r) { return r.json(); })
      .then(function(data) {
        if (!data.jobs || data.jobs.length === 0) {
          console.log('[WTTJ] No jobs need descriptions!');
          alert('All jobs already have descriptions!');
          return;
        }

        // Filter for WTTJ URLs only
        autoFetchQueue = data.jobs.filter(function(job) {
          var jobUrl = job.Url || job.url || '';
          var source = job.Source || job.source || '';
          return (source.toLowerCase() === 'wttj' || jobUrl.includes('welcometothejungle.com'));
        }).map(function(job) {
          return {
            Url: job.Url || job.url || '',
            Title: job.Title || job.title || 'Unknown Job',
            Company: job.Company || job.company || ''
          };
        });

        if (autoFetchQueue.length === 0) {
          console.log('[WTTJ] No WTTJ jobs need descriptions!');
          alert('No Welcome to the Jungle jobs need descriptions.');
          return;
        }

        autoFetchIndex = 0;
        autoFetchEnabled = true;

        sessionStorage.setItem('wttj-autofetch', JSON.stringify({
          enabled: true,
          delay: autoFetchDelay,
          queue: autoFetchQueue,
          index: autoFetchIndex
        }));

        console.log('[WTTJ] Auto-fetch queue: ' + autoFetchQueue.length + ' jobs');
        showAutoFetchUI();

        var firstJob = autoFetchQueue[0];
        updateAutoFetchUI(1, autoFetchQueue.length, firstJob.Title);

        navigateToNextJob();
      })
      .catch(function(e) {
        console.log('[WTTJ] Error starting auto-fetch: ' + e.message);
        alert('Error starting auto-fetch: ' + e.message);
      });
  }

  function stopAutoFetch() {
    autoFetchEnabled = false;
    autoFetchQueue = [];
    autoFetchIndex = 0;
    sessionStorage.removeItem('wttj-autofetch');
    hideAutoFetchUI();
    console.log('[WTTJ] Auto-fetch stopped');
  }

  function checkAutoFetchState() {
    var state = sessionStorage.getItem('wttj-autofetch');
    if (!state) return;

    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled && parsed.queue && parsed.queue.length > 0) {
        autoFetchEnabled = true;
        autoFetchDelay = parsed.delay || 3000;
        autoFetchQueue = parsed.queue;
        autoFetchIndex = parsed.index || 0;

        console.log('[WTTJ] Resuming auto-fetch at job ' + (autoFetchIndex + 1) + ' of ' + autoFetchQueue.length);
        showAutoFetchUI();

        processCurrentPageForAutoFetch();
      }
    } catch (e) {
      console.log('[WTTJ] Error restoring auto-fetch state: ' + e.message);
      sessionStorage.removeItem('wttj-autofetch');
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
      var queueUrl = currentJob && currentJob.Url ? currentJob.Url : '';

      console.log('[WTTJ] Auto-fetch: About to update description', {
        queueUrl: queueUrl,
        descLength: desc ? desc.length : 0,
        descPreview: desc ? desc.substring(0, 80) : '',
        jobTitle: jobTitle
      });

      if (desc && desc.length > 50) {
        updateDescription(queueUrl, desc).then(function(result) {
          console.log('[WTTJ] Auto-fetch: updateDescription result:', result);
          autoFetchIndex++;

          var state = JSON.parse(sessionStorage.getItem('wttj-autofetch') || '{}');
          state.index = autoFetchIndex;
          sessionStorage.setItem('wttj-autofetch', JSON.stringify(state));

          if (autoFetchIndex >= autoFetchQueue.length) {
            completeAutoFetch();
          } else {
            setTimeout(navigateToNextJob, autoFetchDelay);
          }
        });
      } else {
        console.log('[WTTJ] Auto-fetch: No description found, skipping...');
        autoFetchIndex++;

        var state = JSON.parse(sessionStorage.getItem('wttj-autofetch') || '{}');
        state.index = autoFetchIndex;
        sessionStorage.setItem('wttj-autofetch', JSON.stringify(state));

        if (autoFetchIndex >= autoFetchQueue.length) {
          completeAutoFetch();
        } else {
          setTimeout(navigateToNextJob, 1000);
        }
      }
    }, 3000); // Wait longer for React content to load
  }

  function navigateToNextJob() {
    if (!autoFetchEnabled || autoFetchIndex >= autoFetchQueue.length) {
      completeAutoFetch();
      return;
    }

    var job = autoFetchQueue[autoFetchIndex];
    var jobTitle = (job && job.Title) ? job.Title : 'Unknown Job';
    console.log('[WTTJ] Auto-fetch: Navigating to job ' + (autoFetchIndex + 1) + ': ' + jobTitle.substring(0, 30));
    updateAutoFetchUI(autoFetchIndex + 1, autoFetchQueue.length, jobTitle);

    if (job && job.Url) {
      window.location.href = job.Url;
    } else {
      console.log('[WTTJ] Auto-fetch: Invalid job URL, skipping...');
      autoFetchIndex++;
      setTimeout(navigateToNextJob, 500);
    }
  }

  function completeAutoFetch() {
    var totalProcessed = autoFetchIndex;
    stopAutoFetch();
    console.log('[WTTJ] Auto-fetch complete! Processed ' + totalProcessed + ' jobs');
    alert('Auto-fetch complete!\nProcessed ' + totalProcessed + ' jobs.');
  }

  // === Crawl Functions (paginate through search results) ===
  var crawlEnabled = false;
  var crawlDelay = 5000;
  var crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
  var crawlMaxPages = 20;

  function startCrawl(delaySeconds, maxPages) {
    if (crawlEnabled || autoFetchEnabled) {
      console.log('[WTTJ] Crawl or auto-fetch already running');
      return;
    }
    crawlDelay = (delaySeconds || 5) * 1000;
    crawlMaxPages = maxPages || 20;
    crawlResults = { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
    crawlEnabled = true;

    sessionStorage.setItem('wttj-crawl', JSON.stringify({
      enabled: true,
      delay: crawlDelay,
      maxPages: crawlMaxPages,
      results: crawlResults
    }));

    console.log('[WTTJ] Starting crawl with ' + (crawlDelay/1000) + 's delay, max ' + crawlMaxPages + ' pages');
    showCrawlUI();
    processCrawlPage();
  }

  function stopCrawl() {
    crawlEnabled = false;
    sessionStorage.removeItem('wttj-crawl');
    hideCrawlUI();
    console.log('[WTTJ] Crawl stopped. Pages: ' + crawlResults.pagesScanned + ', Found: ' + crawlResults.jobsFound + ', Added: ' + crawlResults.jobsAdded);
  }

  function checkCrawlState() {
    var state = sessionStorage.getItem('wttj-crawl');
    if (!state) return;
    try {
      var parsed = JSON.parse(state);
      if (parsed.enabled) {
        crawlEnabled = true;
        crawlDelay = parsed.delay || 5000;
        crawlMaxPages = parsed.maxPages || 20;
        crawlResults = parsed.results || { pagesScanned: 0, jobsFound: 0, jobsAdded: 0 };
        console.log('[WTTJ] Resuming crawl - page ' + (crawlResults.pagesScanned + 1));
        showCrawlUI();
        processCrawlPage();
      }
    } catch (e) {
      sessionStorage.removeItem('wttj-crawl');
    }
  }

  function processCrawlPage() {
    if (!crawlEnabled) return;
    if (crawlResults.pagesScanned >= crawlMaxPages) {
      completeCrawl();
      return;
    }

    updateCrawlUI();

    // WTTJ is a React SPA, wait longer for content
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

      console.log('[WTTJ] Crawl page ' + (crawlResults.pagesScanned + 1) + ': found ' + pageJobs.length + ' jobs');
      crawlResults.jobsFound += pageJobs.length;
      crawlResults.pagesScanned++;

      if (pageJobs.length === 0) {
        console.log('[WTTJ] No jobs found on page, stopping crawl');
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
          console.log('[WTTJ] Crawl: navigating to next page in ' + (crawlDelay/1000) + 's');
          setTimeout(function() {
            if (crawlEnabled) window.location.href = nextUrl;
          }, crawlDelay);
        } else {
          console.log('[WTTJ] Crawl: no next page found');
          completeCrawl();
        }
      });
    }, 4000); // Longer wait for React SPA
  }

  function findNextPageUrl() {
    // WTTJ pagination: look for next page button/link
    var nextBtn = document.querySelector('a[rel="next"]') ||
                  document.querySelector('[aria-label="Next page"]') ||
                  document.querySelector('[data-testid="pagination-next"]') ||
                  document.querySelector('nav[aria-label*="pagination"] li:last-child a');

    if (nextBtn && nextBtn.href) return nextBtn.href;

    // Try to find page number in URL and increment
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
    sessionStorage.setItem('wttj-crawl', JSON.stringify({
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
    console.log('[WTTJ] ' + msg);
    alert(msg);
  }

  function showCrawlUI() {
    var existing = document.getElementById('wttj-crawl-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = 'wttj-crawl-ui';
    el.innerHTML = '<div id="wttj-cr-status">Crawling search results...</div>' +
                   '<div id="wttj-cr-progress">Page <span id="wttj-cr-page">0</span> | Found <span id="wttj-cr-found">0</span> | Added <span id="wttj-cr-added">0</span></div>' +
                   '<button id="wttj-cr-stop">Stop Crawl</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:#057642;color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#wttj-cr-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#wttj-cr-progress').style.cssText = 'margin-bottom:12px;';
    el.querySelector('#wttj-cr-stop').style.cssText = 'background:#dc3545;border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#wttj-cr-stop').onclick = stopCrawl;
    document.body.appendChild(el);
  }

  function updateCrawlUI() {
    var p = document.getElementById('wttj-cr-page');
    var f = document.getElementById('wttj-cr-found');
    var a = document.getElementById('wttj-cr-added');
    if (p) p.textContent = crawlResults.pagesScanned;
    if (f) f.textContent = crawlResults.jobsFound;
    if (a) a.textContent = crawlResults.jobsAdded;
  }

  function hideCrawlUI() {
    var el = document.getElementById('wttj-crawl-ui');
    if (el) el.remove();
  }

  setTimeout(checkCrawlState, 2500);

  // Expose API
  window.WTTJ = {
    extract: doExtract,
    status: function() { return { sent: totalSent, dup: totalDuplicates }; },
    test: function() {
      fetch(SERVER_URL + '/api/jobs', {
        headers: getHeaders()
      })
        .then(function(r) { return r.json(); })
        .then(function(d) { console.log('[WTTJ] Server has ' + d.length + ' jobs'); })
        .catch(function(e) { console.log('[WTTJ] Error: ' + e.message); });
    },
    getDesc: function() {
      var desc = getJobDescription();
      console.log('[WTTJ] Current URL:', getCurrentJobUrl());
      console.log('[WTTJ] Current Job ID:', getCurrentJobId());
      console.log('[WTTJ] Description (' + desc.length + ' chars):', desc.substring(0, 300));
      return desc;
    },
    updateDesc: function() {
      var desc = getJobDescription();
      var url = getCurrentJobUrl();
      if (desc && url) {
        updateDescription(url, desc);
        console.log('[WTTJ] Sent description update');
      } else {
        console.log('[WTTJ] No description or URL found');
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
      console.log('[WTTJ] === DEBUG ===');
      console.log('[WTTJ] URL:', window.location.href);
      console.log('[WTTJ] Server URL:', SERVER_URL);
      console.log('[WTTJ] API Key:', API_KEY ? '(set)' : '(not set)');
      console.log('[WTTJ] Current Job ID:', getCurrentJobId());

      console.log('[WTTJ] Checking selectors...');
      var cards = findAllJobCards();
      console.log('[WTTJ] Total cards found:', cards.length);

      if (cards.length > 0) {
        console.log('[WTTJ] First 3 jobs:');
        for (var i = 0; i < Math.min(3, cards.length); i++) {
          var job = extractJobFromCard(cards[i]);
          console.log('  ' + (i+1) + '. "' + (job ? job.Title.substring(0, 40) : 'NO TITLE') + '" at "' + (job ? job.Company : 'NO COMPANY') + '" - ' + (job ? job.Location : 'NO LOC'));
        }
      }

      console.log('[WTTJ] Description length:', getJobDescription().length);
      console.log('[WTTJ] Auto-fetch status:', window.WTTJ.autoFetchStatus());

      // Log page structure for debugging selectors
      console.log('[WTTJ] === Page structure (data-testid) ===');
      console.log('  [data-testid="job-title"]:', document.querySelector('[data-testid="job-title"]') ? document.querySelector('[data-testid="job-title"]').textContent.substring(0, 50) : 'none');
      console.log('  [data-testid="job-card-v2"] count:', document.querySelectorAll('[data-testid="job-card-v2"]').length);
      console.log('  [data-testid="company-logo"] img:', document.querySelector('[data-testid="company-logo"] img') ? document.querySelector('[data-testid="company-logo"] img').alt : 'none');
      console.log('  [data-testid="job-location-tag"] count:', document.querySelectorAll('[data-testid="job-location-tag"]').length);
      console.log('  [data-testid="salary-section"]:', document.querySelector('[data-testid="salary-section"]') ? document.querySelector('[data-testid="salary-section"]').textContent.substring(0, 30) : 'none');
      console.log('  [data-testid="job-technology-used"] tags:', document.querySelectorAll('[data-testid="job-technology-used"] > div').length);
      console.log('  [data-testid="job-requirement-bullet"] count:', document.querySelectorAll('[data-testid="job-requirement-bullet"]').length);

      // Log generic page structure for search results pages
      console.log('[WTTJ] === Page structure (generic) ===');
      console.log('  a[href*="/jobs/"] count:', document.querySelectorAll('a[href*="/jobs/"]').length);
      console.log('  article count:', document.querySelectorAll('article').length);
      console.log('  li count:', document.querySelectorAll('li').length);
      console.log('  h1 count:', document.querySelectorAll('h1').length);
      console.log('  h2 count:', document.querySelectorAll('h2').length);
      console.log('  h3 count:', document.querySelectorAll('h3').length);

      // Sample job links found
      var jobLinks = document.querySelectorAll('a[href*="/jobs/"]');
      console.log('[WTTJ] Sample job links:');
      for (var j = 0; j < Math.min(3, jobLinks.length); j++) {
        console.log('  ' + (j+1) + '. ' + jobLinks[j].href.substring(0, 80));
      }

      fetch(SERVER_URL + '/api/jobs/needing-descriptions?limit=5', {
        headers: getHeaders()
      })
        .then(function(r) { return r.json(); })
        .then(function(d) {
          var wttjJobs = d.jobs ? d.jobs.filter(function(j) {
            var url = j.Url || j.url || '';
            var source = j.Source || j.source || '';
            return source.toLowerCase() === 'wttj' || url.includes('welcometothejungle.com');
          }) : [];
          console.log('[WTTJ] WTTJ jobs needing descriptions:', wttjJobs.length);
        })
        .catch(function(e) { console.log('[WTTJ] Error checking: ' + e.message); });
    },
    help: function() {
      console.log('[WTTJ] === COMMANDS ===');
      console.log('WTTJ.extract()         - Extract jobs from current page');
      console.log('WTTJ.debug()           - Show debug info');
      console.log('WTTJ.test()            - Test server connection');
      console.log('WTTJ.status()          - Show send statistics');
      console.log('WTTJ.getDesc()         - Get current job description');
      console.log('WTTJ.updateDesc()      - Send current description to server');
      console.log('WTTJ.fetchMissing()    - Check for jobs needing descriptions');
      console.log('');
      console.log('=== AUTO-FETCH ===');
      console.log('WTTJ.autoFetch(3)      - Start auto-fetching descriptions (3s delay)');
      console.log('WTTJ.stopAutoFetch()   - Stop auto-fetching');
      console.log('WTTJ.autoFetchStatus() - Check auto-fetch progress');
      console.log('');
      console.log('=== CRAWL ===');
      console.log('WTTJ.crawl(5, 20)      - Crawl search results (5s delay, 20 pages max)');
      console.log('WTTJ.stopCrawl()       - Stop crawling');
      console.log('WTTJ.crawlStatus()     - Check crawl progress');
    }
  };

  console.log('[WTTJ] Ready! Type WTTJ.help() for commands.');
})();
