// Popup script for LinkedIn Job Extractor

var SERVER_URL = 'https://localhost:7046';
var API_KEY = '';
var autoFetchCheckInterval = null;

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
    document.getElementById('serverUrl').value = url;
    SERVER_URL = url;
    
    if (result.apiKey) {
      API_KEY = result.apiKey;
      document.getElementById('apiKey').value = result.apiKey;
      updateApiKeyStatus(true);
    } else {
      updateApiKeyStatus(false);
    }
    
    var delay = result.autoFetchDelay || 5;
    document.getElementById('autoFetchDelay').value = delay;
    updateDelayWarning(delay);
    
    testConnection(url);
    loadStats(url);
    checkAutoFetchStatus();
  });

  // Save URL when changed
  document.getElementById('serverUrl').addEventListener('change', function() {
    var url = (this.value.trim() || SERVER_URL).replace(/\/+$/, '');
    SERVER_URL = url;
    chrome.storage.local.set({ serverUrl: url });
    testConnection(url);
    loadStats(url);
  });

  // Save API key when changed
  document.getElementById('apiKey').addEventListener('change', function() {
    API_KEY = this.value.trim();
    chrome.storage.local.set({ apiKey: API_KEY });
    updateApiKeyStatus(!!API_KEY);
    testConnection(SERVER_URL);
    loadStats(SERVER_URL);
  });

  // Save delay when changed and show/hide warning
  document.getElementById('autoFetchDelay').addEventListener('change', function() {
    var delay = parseInt(this.value) || 5;
    chrome.storage.local.set({ autoFetchDelay: delay });
    updateDelayWarning(delay);
  });

  // Also check on input (as user types)
  document.getElementById('autoFetchDelay').addEventListener('input', function() {
    var delay = parseInt(this.value) || 5;
    updateDelayWarning(delay);
  });

  // Button event listeners
  document.getElementById('testConnectionBtn').addEventListener('click', function() {
    var url = document.getElementById('serverUrl').value.trim() || SERVER_URL;
    testConnection(url);
  });

  document.getElementById('refreshStatsBtn').addEventListener('click', function() {
    var url = document.getElementById('serverUrl').value.trim() || SERVER_URL;
    loadStats(url);
    showStatus('Stats refreshed', 'success');
  });

  document.getElementById('extractBtn').addEventListener('click', extractNow);
  
  document.getElementById('startAutoFetchBtn').addEventListener('click', startAutoFetch);
  document.getElementById('stopAutoFetchBtn').addEventListener('click', stopAutoFetch);
  
  document.getElementById('startCrawlBtn').addEventListener('click', startCrawl);
  document.getElementById('stopCrawlBtn').addEventListener('click', stopCrawlFromPopup);

  document.getElementById('openLinkedInBtn').addEventListener('click', function() {
    chrome.tabs.create({ url: 'https://www.linkedin.com/jobs/' });
  });

  document.getElementById('openAppBtn').addEventListener('click', function() {
    var url = document.getElementById('serverUrl').value.trim() || SERVER_URL;
    chrome.tabs.create({ url: url });
  });

  document.getElementById('goToLinkedInBtn').addEventListener('click', function() {
    chrome.tabs.create({ url: 'https://www.linkedin.com/jobs/' });
  });

  // Check if on LinkedIn
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    if (!tab || !tab.url || tab.url.indexOf('linkedin.com') === -1) {
      document.getElementById('linkedin-content').style.display = 'none';
      document.getElementById('not-linkedin').style.display = 'block';
    }
  });

  // Check auto-fetch status periodically
  autoFetchCheckInterval = setInterval(checkAutoFetchStatus, 2000);
});

function testConnection(url) {
  var dot = document.getElementById('connectionDot');
  var text = document.getElementById('connectionText');
  
  url = (url || SERVER_URL).replace(/\/$/, '');
  text.textContent = 'Testing...';
  dot.className = 'connection-dot';

  fetch(url + '/api/jobs/stats', {
    method: 'GET',
    headers: getHeaders()
  })
  .then(function(response) {
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return response.json();
  })
  .then(function(stats) {
    dot.className = 'connection-dot connected';
    text.textContent = 'Connected';
    updateStatsDisplay(stats);
  })
  .catch(function(error) {
    dot.className = 'connection-dot disconnected';
    text.textContent = 'Failed: ' + error.message;
  });
}

function loadStats(url) {
  url = (url || document.getElementById('serverUrl').value.trim() || SERVER_URL).replace(/\/$/, '');
  
  fetch(url + '/api/jobs/stats', {
    headers: getHeaders()
  })
    .then(function(r) { return r.json(); })
    .then(function(stats) {
      updateStatsDisplay(stats);
    })
    .catch(function(e) {
      console.log('Error loading stats:', e);
    });
}

function updateStatsDisplay(stats) {
  document.getElementById('totalJobs').textContent = stats.TotalJobs || stats.totalJobs || 0;
  document.getElementById('appliedCount').textContent = stats.AppliedCount || stats.appliedCount || 0;
  document.getElementById('needDescCount').textContent = stats.NeedingDescriptionCount || stats.needingDescriptionCount || 0;
  
  // Highlight if there are jobs needing descriptions
  var needDescItem = document.getElementById('needDescItem');
  var needDescCount = stats.NeedingDescriptionCount || stats.needingDescriptionCount || 0;
  if (needDescCount > 0) {
    needDescItem.classList.add('warning');
  } else {
    needDescItem.classList.remove('warning');
  }
}

function extractNow() {
  var btn = document.getElementById('extractBtn');
  btn.disabled = true;
  btn.innerHTML = '? Extracting...';

  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    
    if (!tab || !tab.url || tab.url.indexOf('linkedin.com') === -1) {
      showStatus('Please navigate to LinkedIn first', 'error');
      btn.disabled = false;
      btn.innerHTML = '?? Extract Jobs Now';
      return;
    }

    // Send message to content script
    chrome.tabs.sendMessage(tab.id, { action: 'extract' }, function(response) {
      if (chrome.runtime.lastError) {
        showStatus('Extension not loaded. Refresh the page.', 'error');
      } else {
        showStatus('Extraction triggered!', 'success');
        var url = document.getElementById('serverUrl').value.trim() || SERVER_URL;
        setTimeout(function() { loadStats(url); }, 2000);
      }
      btn.disabled = false;
      btn.innerHTML = '?? Extract Jobs Now';
    });
  });
}

function startAutoFetch() {
  var delay = parseInt(document.getElementById('autoFetchDelay').value) || 5;
  
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    
    if (!tab || !tab.url || tab.url.indexOf('linkedin.com') === -1) {
      showStatus('Please navigate to LinkedIn first', 'error');
      return;
    }

    // Execute the auto-fetch command in the content script
    chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: function(delaySeconds) {
        if (window.LJE && window.LJE.autoFetch) {
          window.LJE.autoFetch(delaySeconds);
          return { success: true };
        } else {
          return { success: false, error: 'Extension not loaded' };
        }
      },
      args: [delay]
    }, function(results) {
      if (chrome.runtime.lastError) {
        showStatus('Error: ' + chrome.runtime.lastError.message, 'error');
        return;
      }
      
      if (results && results[0] && results[0].result && results[0].result.success) {
        showStatus('Auto-fetch started with ' + delay + 's delay', 'success');
        setTimeout(checkAutoFetchStatus, 1000);
      } else {
        showStatus('Extension not loaded. Refresh the page.', 'error');
      }
    });
  });
}

function stopAutoFetch() {
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    
    if (!tab) return;

    chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: function() {
        if (window.LJE && window.LJE.stopAutoFetch) {
          window.LJE.stopAutoFetch();
          return { success: true };
        }
        return { success: false };
      }
    }, function(results) {
      if (results && results[0] && results[0].result && results[0].result.success) {
        showStatus('Auto-fetch stopped', 'info');
        hideAutoFetchStatus();
      }
    });
  });
}

function checkAutoFetchStatus() {
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    
    if (!tab || !tab.url || tab.url.indexOf('linkedin.com') === -1) {
      hideAutoFetchStatus();
      return;
    }

    chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: function() {
        if (window.LJE && window.LJE.autoFetchStatus) {
          return window.LJE.autoFetchStatus();
        }
        return null;
      }
    }, function(results) {
      if (chrome.runtime.lastError) {
        hideAutoFetchStatus();
        return;
      }
      
      if (results && results[0] && results[0].result) {
        var status = results[0].result;
        if (status.enabled && status.total > 0) {
          showAutoFetchStatus(status);
        } else {
          hideAutoFetchStatus();
        }
      } else {
        hideAutoFetchStatus();
      }
    });
  });
}

function showAutoFetchStatus(status) {
  var statusDiv = document.getElementById('autofetchStatus');
  var progressSpan = document.getElementById('afProgress');
  var statusText = document.getElementById('afStatusText');
  
  statusDiv.classList.remove('hidden');
  document.getElementById('autoFetchSection').classList.add('hidden');
  
  progressSpan.textContent = (status.current + 1) + '/' + status.total;
  statusText.textContent = 'Auto-fetching... (' + status.delay + 's delay)';
}

function hideAutoFetchStatus() {
  document.getElementById('autofetchStatus').classList.add('hidden');
  document.getElementById('autoFetchSection').classList.remove('hidden');
}

function updateDelayWarning(delay) {
  var warning = document.getElementById('delayWarning');
  if (!warning) return;
  
  if (delay < 10) {
    // Very low - high risk
    warning.classList.remove('hidden');
    warning.classList.add('danger');
    warning.innerHTML = '?? <strong>Very risky!</strong> LinkedIn will likely detect and block you. Use 20+ seconds.';
  } else if (delay < 20) {
    // Low - moderate risk
    warning.classList.remove('hidden');
    warning.classList.remove('danger');
    warning.innerHTML = '?? Low delay! LinkedIn may detect automated browsing. Consider using 20+ seconds to be safe.';
  } else {
    // Safe
    warning.classList.add('hidden');
    warning.classList.remove('danger');
  }
}

function updateApiKeyStatus(hasKey) {
  const statusEl = document.getElementById('api-key-status');
  if (statusEl) {
    statusEl.textContent = hasKey ? '? Key Set' : '? No Key';
    statusEl.style.color = hasKey ? '#4ade80' : '#fbbf24';
  }
}

// Cleanup interval on popup close
window.addEventListener('unload', function() {
  if (autoFetchCheckInterval) {
    clearInterval(autoFetchCheckInterval);
  }
});

function showStatus(message, type) {
  var status = document.getElementById('status');
  status.textContent = message;
  status.className = 'status show ' + type;
  setTimeout(function() {
    status.className = 'status';
  }, 3000);
}

function startCrawl() {
  var delay = parseInt(document.getElementById('autoFetchDelay').value) || 5;

  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    if (!tab || !tab.url || tab.url.indexOf('linkedin.com') === -1) {
      showStatus('Please navigate to LinkedIn Jobs search first', 'error');
      return;
    }

    chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: function(delaySeconds) {
        if (window.LJE && window.LJE.crawl) {
          window.LJE.crawl(delaySeconds, 20);
          return { success: true };
        }
        return { success: false, error: 'Extension not loaded' };
      },
      args: [delay]
    }, function(results) {
      if (chrome.runtime.lastError) {
        showStatus('Error: ' + chrome.runtime.lastError.message, 'error');
        return;
      }
      if (results && results[0] && results[0].result && results[0].result.success) {
        showStatus('Crawl started with ' + delay + 's delay', 'success');
      } else {
        showStatus('Extension not loaded. Refresh the page.', 'error');
      }
    });
  });
}

function stopCrawlFromPopup() {
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    var tab = tabs[0];
    if (!tab) return;

    chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: function() {
        if (window.LJE && window.LJE.stopCrawl) {
          window.LJE.stopCrawl();
          return { success: true };
        }
        return { success: false };
      }
    }, function(results) {
      if (results && results[0] && results[0].result && results[0].result.success) {
        showStatus('Crawl stopped', 'info');
        document.getElementById('crawlStatus').classList.add('hidden');
      }
    });
  });
}

document.getElementById('checkAvailabilityBtn').addEventListener('click', function() {
    var btn = document.getElementById('checkAvailabilityBtn');
    var resultDiv = document.getElementById('availabilityResult');
    var url = document.getElementById('serverUrl').value.trim() || SERVER_URL;

    btn.disabled = true;
    btn.innerHTML = '⏳ Checking...';
    resultDiv.className = 'status show info';
    resultDiv.textContent = 'Checking job availability...';

    fetch(url.replace(/\/$/, '') + '/api/jobs/check-availability', {
      method: 'POST',
      headers: Object.assign({ 'Content-Type': 'application/json' }, getHeaders()),
      body: JSON.stringify({ source: 'LinkedIn' })
    })
    .then(function(r) { return r.json(); })
    .then(function(d) {
      btn.disabled = false;
      btn.innerHTML = '🔍 Check Job Availability';
      if (d.total === 0) {
        resultDiv.className = 'status show info';
        resultDiv.textContent = 'No jobs to check (all checked recently).';
      } else {
        var msg = 'Checked ' + d.checked + ' jobs.';
        if (d.markedUnavailable > 0) msg += ' ' + d.markedUnavailable + ' marked unavailable.';
        if (d.errors > 0) msg += ' ' + d.errors + ' errors.';
        if (d.skipped > 0) msg += ' ' + d.skipped + ' skipped.';
        resultDiv.className = 'status show ' + (d.markedUnavailable > 0 ? 'success' : 'info');
        resultDiv.textContent = msg;
      }
      loadStats(url);
    })
    .catch(function(e) {
      btn.disabled = false;
      btn.innerHTML = '🔍 Check Job Availability';
      resultDiv.className = 'status show error';
      resultDiv.textContent = 'Error: ' + e.message;
    });
  });

document.getElementById('removeDuplicatesBtn').addEventListener('click', function() {
    var url = document.getElementById('serverUrl').value.trim() || SERVER_URL;
    if (!confirm('Remove duplicate jobs? This cannot be undone.')) return;
    showStatus('Removing duplicates...', 'info');
    fetch(url.replace(/\/$/, '') + '/api/jobs/remove-duplicates', {
      method: 'POST',
      headers: getHeaders()
    })
    .then(function(r) { return r.json(); })
    .then(function(d) {
      if (d.removed > 0) {
        showStatus('Removed ' + d.removed + ' duplicate jobs.', 'success');
        loadStats(url);
      } else {
        showStatus('No duplicates found.', 'info');
      }
    })
    .catch(function(e) {
      showStatus('Error removing duplicates: ' + e.message, 'error');
    });
  });
