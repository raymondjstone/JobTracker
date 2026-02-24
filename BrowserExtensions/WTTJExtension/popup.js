var SERVER_URL = 'https://localhost:7046';
var API_KEY = '';

// Helper function to get headers with API key
function getHeaders() {
  var headers = { 'Accept': 'application/json' };
  if (API_KEY) {
    headers['X-API-Key'] = API_KEY;
  }
  return headers;
}

document.addEventListener('DOMContentLoaded', function() {
  // Load saved URL, API key, and delay
  chrome.storage.local.get(['serverUrl', 'apiKey', 'autoFetchDelay'], function(result) {
    var url = (result.serverUrl || SERVER_URL).replace(/\/+$/, '');
    document.getElementById('server-url').value = url;
    SERVER_URL = url;

    if (result.apiKey) {
      API_KEY = result.apiKey;
      document.getElementById('api-key').value = result.apiKey;
    }

    var delay = result.autoFetchDelay || 5;
    document.getElementById('autoFetchDelay').value = delay;
    updateDelayWarning(delay);

    checkServerStatus();
    updateSentCount();
  });

  // Save URL when changed
  document.getElementById('server-url').addEventListener('change', function() {
    var url = (this.value.trim() || 'https://localhost:7046').replace(/\/+$/, '');
    chrome.storage.local.set({ serverUrl: url });
    SERVER_URL = url;
    checkServerStatus();
  });

  // Save API key when changed
  document.getElementById('api-key').addEventListener('change', function() {
    API_KEY = this.value.trim();
    chrome.storage.local.set({ apiKey: API_KEY });
    checkServerStatus();
  });

  // Save delay when changed and show/hide warning
  document.getElementById('autoFetchDelay').addEventListener('change', function() {
    var delay = parseInt(this.value) || 5;
    chrome.storage.local.set({ autoFetchDelay: delay });
    updateDelayWarning(delay);
  });
  document.getElementById('autoFetchDelay').addEventListener('input', function() {
    updateDelayWarning(parseInt(this.value) || 5);
  });

  document.getElementById('extract-btn').addEventListener('click', extractJobs);
  document.getElementById('crawl-btn').addEventListener('click', startCrawl);
  document.getElementById('auto-fetch-btn').addEventListener('click', startAutoFetch);
  document.getElementById('check-availability-btn').addEventListener('click', checkAvailability);
  document.getElementById('open-tracker-btn').addEventListener('click', openTracker);
});

async function checkServerStatus() {
  const statusEl = document.getElementById('server-status');
  const countEl = document.getElementById('job-count');
  const needDescEl = document.getElementById('need-desc');
  const dot = document.getElementById('connection-dot');
  const text = document.getElementById('connection-text');

  try {
    const response = await fetch(SERVER_URL + '/api/jobs/stats', {
      headers: getHeaders()
    });
    if (response.ok) {
      const stats = await response.json();
      statusEl.textContent = 'Connected';
      statusEl.className = 'status-value connected';
      countEl.textContent = stats.totalJobs || 0;
      needDescEl.textContent = stats.needingDescriptionCount || 0;
      dot.className = 'connection-dot connected';
      text.textContent = 'Connected';
    } else {
      throw new Error('Server error');
    }
  } catch (e) {
    statusEl.textContent = 'Offline';
    statusEl.className = 'status-value error';
    countEl.textContent = '-';
    needDescEl.textContent = '-';
    dot.className = 'connection-dot disconnected';
    text.textContent = 'Offline';
  }
}

async function updateSentCount() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab && tab.url && tab.url.includes('welcometothejungle.com')) {
      const results = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => window.WTTJ ? window.WTTJ.status() : { sent: 0, dup: 0 }
      });
      if (results && results[0] && results[0].result) {
        document.getElementById('sent-count').textContent = results[0].result.sent || 0;
      }
    }
  } catch (e) {
    console.log('Could not get sent count:', e);
  }
}

async function extractJobs() {
  const btn = document.getElementById('extract-btn');
  btn.textContent = 'Extracting...';
  btn.disabled = true;

  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || !tab.url || !tab.url.includes('welcometothejungle.com')) {
      alert('Please navigate to Welcome to the Jungle first!');
      btn.textContent = 'Extract Jobs Now';
      btn.disabled = false;
      return;
    }

    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        if (window.WTTJ) {
          window.WTTJ.extract();
        } else {
          console.log('WTTJ extractor not loaded');
        }
      }
    });

    setTimeout(() => {
      btn.textContent = 'Extract Jobs Now';
      btn.disabled = false;
      updateSentCount();
      checkServerStatus();
    }, 2000);
  } catch (e) {
    console.error('Error extracting:', e);
    btn.textContent = 'Extract Jobs Now';
    btn.disabled = false;
    alert('Error: ' + e.message);
  }
}

async function checkAvailability() {
  var btn = document.getElementById('check-availability-btn');
  var resultDiv = document.getElementById('availability-result');

  btn.disabled = true;
  btn.textContent = 'Checking...';
  resultDiv.style.display = 'block';
  resultDiv.textContent = 'Checking job availability...';

  try {
    var headers = getHeaders();
    headers['Content-Type'] = 'application/json';
    const response = await fetch(SERVER_URL + '/api/jobs/check-availability', {
      method: 'POST',
      headers: headers,
      body: JSON.stringify({ source: 'WTTJ' })
    });
    const d = await response.json();
    if (d.total === 0) {
      resultDiv.textContent = 'No jobs to check (all checked recently).';
    } else {
      var msg = 'Checked ' + d.checked + ' jobs.';
      if (d.markedUnavailable > 0) msg += ' ' + d.markedUnavailable + ' marked unavailable.';
      if (d.errors > 0) msg += ' ' + d.errors + ' errors.';
      if (d.skipped > 0) msg += ' ' + d.skipped + ' skipped.';
      resultDiv.textContent = msg;
    }
    checkServerStatus();
  } catch (e) {
    resultDiv.style.background = '#fee2e2';
    resultDiv.style.color = '#dc2626';
    resultDiv.textContent = 'Error: ' + e.message;
  }

  btn.disabled = false;
  btn.textContent = 'Check Job Availability';
}

function updateDelayWarning(delay) {
  var warning = document.getElementById('delayWarning');
  if (!warning) return;

  if (delay < 10) {
    warning.className = 'delay-warning show danger';
    warning.textContent = 'Very risky! The site may detect and block automated browsing. Use 20+ seconds to be safe.';
  } else if (delay < 20) {
    warning.className = 'delay-warning show';
    warning.textContent = 'Low delay! The site may detect automated browsing. Consider using 20+ seconds.';
  } else {
    warning.className = 'delay-warning';
  }
}

async function startCrawl() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || !tab.url || !tab.url.includes('welcometothejungle.com')) {
      alert('Please navigate to Welcome to the Jungle search results first!');
      return;
    }

    var delay = parseInt(document.getElementById('autoFetchDelay').value) || 5;

    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: (delaySeconds) => {
        if (window.WTTJ) {
          window.WTTJ.crawl(delaySeconds, 20);
        }
      },
      args: [delay]
    });

    var btn = document.getElementById('crawl-btn');
    btn.textContent = 'Crawl Started! (' + delay + 's)';
    setTimeout(() => {
      btn.textContent = 'Crawl Search Results';
    }, 2000);
  } catch (e) {
    console.error('Error starting crawl:', e);
    alert('Error: ' + e.message);
  }
}

async function startAutoFetch() {
  const btn = document.getElementById('auto-fetch-btn');

  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || !tab.url || !tab.url.includes('welcometothejungle.com')) {
      alert('Please navigate to Welcome to the Jungle first!');
      return;
    }

    var delay = parseInt(document.getElementById('autoFetchDelay').value) || 5;

    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: (delaySeconds) => {
        if (window.WTTJ) {
          window.WTTJ.autoFetch(delaySeconds);
        }
      },
      args: [delay]
    });

    btn.textContent = 'Auto-Fetch Started! (' + delay + 's)';
    setTimeout(() => {
      btn.textContent = 'Auto-Fetch Descriptions';
    }, 2000);
  } catch (e) {
    console.error('Error starting auto-fetch:', e);
    alert('Error: ' + e.message);
  }
}

function openTracker() {
  chrome.tabs.create({ url: SERVER_URL });
}
