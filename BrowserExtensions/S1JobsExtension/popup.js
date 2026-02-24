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
    loadStats();
  });

  // Save URL when changed
  document.getElementById('server-url').addEventListener('change', function() {
    var url = (this.value.trim() || 'https://localhost:7046').replace(/\/+$/, '');
    chrome.storage.local.set({ serverUrl: url });
    SERVER_URL = url;
    checkServerStatus();
    loadStats();
  });

  // Save API key when changed
  document.getElementById('api-key').addEventListener('change', function() {
    API_KEY = this.value.trim();
    chrome.storage.local.set({ apiKey: API_KEY });
    checkServerStatus();
    loadStats();
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
  document.getElementById('autofetch-btn').addEventListener('click', startAutoFetch);
  document.getElementById('check-availability-btn').addEventListener('click', checkAvailability);
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
      document.getElementById('need-desc').textContent = stats.needingDescriptionCount || 0;
    }
  } catch (e) {
    document.getElementById('sent-count').textContent = '-';
    document.getElementById('need-desc').textContent = '-';
  }
}

async function extractJobs() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

  if (!tab.url.includes('s1jobs.com')) {
    showMessage('Please navigate to S1Jobs.com first', true);
    return;
  }

  try {
    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        if (window.S1J && window.S1J.extract) {
          window.S1J.extract();
          return true;
        }
        return false;
      }
    });
    showMessage('Extracting jobs...');
    setTimeout(loadStats, 2000);
  } catch (e) {
    showMessage('Error: ' + e.message, true);
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
      body: JSON.stringify({ source: 'S1Jobs' })
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
    loadStats();
  } catch (e) {
    resultDiv.style.background = 'rgba(192,57,43,0.5)';
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
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

  if (!tab.url.includes('s1jobs.com')) {
    showMessage('Please navigate to S1Jobs.com search results first', true);
    return;
  }

  var delay = parseInt(document.getElementById('autoFetchDelay').value) || 5;

  try {
    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: (delaySeconds) => {
        if (window.S1J && window.S1J.crawl) {
          window.S1J.crawl(delaySeconds, 20);
          return true;
        }
        return false;
      },
      args: [delay]
    });
    showMessage('Crawl started with ' + delay + 's delay...');
  } catch (e) {
    showMessage('Error: ' + e.message, true);
  }
}

async function startAutoFetch() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

  if (!tab.url.includes('s1jobs.com')) {
    showMessage('Please navigate to S1Jobs.com first', true);
    return;
  }

  var delay = parseInt(document.getElementById('autoFetchDelay').value) || 5;

  try {
    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: (delaySeconds) => {
        if (window.S1J && window.S1J.autoFetch) {
          window.S1J.autoFetch(delaySeconds);
          return true;
        }
        return false;
      },
      args: [delay]
    });
    showMessage('Starting auto-fetch with ' + delay + 's delay...');
  } catch (e) {
    showMessage('Error: ' + e.message, true);
  }
}
