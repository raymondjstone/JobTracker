var SERVER_URL = 'https://localhost:7046';
var API_KEY = '';

function getHeaders() {
  var headers = { 'Accept': 'application/json' };
  if (API_KEY) {
    headers['X-API-Key'] = API_KEY;
  }
  return headers;
}

document.addEventListener('DOMContentLoaded', function() {
  chrome.storage.local.get(['serverUrl', 'apiKey'], function(result) {
    var url = (result.serverUrl || SERVER_URL).replace(/\/+$/, '');
    document.getElementById('server-url').value = url;
    SERVER_URL = url;

    if (result.apiKey) {
      API_KEY = result.apiKey;
      document.getElementById('api-key').value = result.apiKey;
    }

    checkServerStatus();
    loadStats();
    checkSchemaStatus();
  });

  document.getElementById('server-url').addEventListener('change', function() {
    var url = (this.value.trim() || 'https://localhost:7046').replace(/\/+$/, '');
    chrome.storage.local.set({ serverUrl: url });
    SERVER_URL = url;
    checkServerStatus();
    loadStats();
  });

  document.getElementById('api-key').addEventListener('change', function() {
    API_KEY = this.value.trim();
    chrome.storage.local.set({ apiKey: API_KEY });
    checkServerStatus();
    loadStats();
  });

  document.getElementById('extract-btn').addEventListener('click', extractJobs);
  document.getElementById('open-app-btn').addEventListener('click', function() {
    chrome.tabs.create({ url: SERVER_URL });
  });
});

function showMessage(text, isError) {
  const msg = document.getElementById('message');
  msg.textContent = text;
  msg.style.display = 'block';
  msg.style.background = isError ? 'rgba(192,57,43,0.5)' : 'rgba(255,255,255,0.2)';
  setTimeout(() => { msg.style.display = 'none'; }, 3000);
}

async function checkServerStatus() {
  var dot = document.getElementById('connection-dot');
  var text = document.getElementById('connection-text');

  try {
    const response = await fetch(SERVER_URL + '/api/jobs/count', {
      headers: getHeaders()
    });
    if (response.ok) {
      document.getElementById('server-status').textContent = 'Connected';
      document.getElementById('server-status').style.color = '#4ade80';
      dot.className = 'connection-dot connected';
      text.textContent = 'Connected';
    } else {
      throw new Error('Server error');
    }
  } catch (e) {
    document.getElementById('server-status').textContent = 'Offline';
    document.getElementById('server-status').style.color = '#fbbf24';
    dot.className = 'connection-dot disconnected';
    text.textContent = 'Offline';
  }
}

async function loadStats() {
  try {
    const response = await fetch(SERVER_URL + '/api/jobs/stats', {
      headers: getHeaders()
    });
    if (response.ok) {
      const stats = await response.json();
      document.getElementById('sent-count').textContent = stats.totalJobs || 0;
    }
  } catch (e) {
    document.getElementById('sent-count').textContent = '-';
  }
}

async function checkSchemaStatus() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || !tab.id) return;

    const results = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        // Count JSON-LD JobPostings
        var jsonLdCount = 0;
        var scripts = document.querySelectorAll('script[type="application/ld+json"]');
        for (var i = 0; i < scripts.length; i++) {
          try {
            var data = JSON.parse(scripts[i].textContent);
            if (data && data['@type'] === 'JobPosting') jsonLdCount++;
            if (Array.isArray(data)) data.forEach(function(d) { if (d && d['@type'] === 'JobPosting') jsonLdCount++; });
            if (data && data['@graph']) data['@graph'].forEach(function(d) { if (d && d['@type'] === 'JobPosting') jsonLdCount++; });
          } catch (e) {}
        }
        // Count job detail links on page
        var jobUrlPattern = /\/jobs?\/(?:view\/|[a-z0-9-]+\/[a-z0-9-])|[?&](?:jobId|job_id|jk)=/i;
        var allLinks = document.querySelectorAll('a[href]');
        var seenHrefs = {};
        var cardCount = 0;
        for (var l = 0; l < allLinks.length; l++) {
          try {
            var u = new URL(allLinks[l].href);
            if (u.hostname === window.location.hostname && jobUrlPattern.test(u.pathname + u.search)) {
              var key = u.origin + u.pathname;
              if (!seenHrefs[key]) { seenHrefs[key] = true; cardCount++; }
            }
          } catch(e) {}
        }
        return { jsonLd: jsonLdCount, cards: cardCount };
      }
    });

    if (results && results[0] && results[0].result) {
      const info = results[0].result;
      const statusEl = document.getElementById('schema-status');
      if (info.jsonLd > 0) {
        statusEl.innerHTML = '<span class="schema-badge detected">&#10003; ' + info.jsonLd + ' job(s) via JSON-LD</span>';
      } else if (info.cards > 0) {
        statusEl.innerHTML = '<span class="schema-badge detected">&#10003; ' + info.cards + ' job link(s) found</span>';
      } else {
        statusEl.innerHTML = '<span class="schema-badge not-detected">&#10007; No jobs detected</span>';
      }
    }
  } catch (e) {
    document.getElementById('schema-status').textContent = 'N/A';
  }
}

async function extractJobs() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || !tab.id) {
      showMessage('No active tab found', true);
      return;
    }

    const results = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        if (window.GEN && window.GEN.extract) {
          window.GEN.extract();
          return true;
        }
        return false;
      }
    });

    if (results && results[0] && results[0].result) {
      showMessage('Extracting jobs...');
      setTimeout(() => {
        loadStats();
        checkSchemaStatus();
      }, 2000);
    } else {
      showMessage('Content script not loaded. Try refreshing the page.', true);
    }
  } catch (e) {
    showMessage('Error: ' + e.message, true);
  }
}
