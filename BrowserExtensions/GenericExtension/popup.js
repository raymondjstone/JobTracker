var SERVER_URL = 'http://localhost:7046';
var API_KEY = '';
var allowedSites = [];
var blockedSites = [];
var currentTabDomain = '';

function getHeaders() {
  var headers = { 'Accept': 'application/json' };
  if (API_KEY) {
    headers['X-API-Key'] = API_KEY;
  }
  return headers;
}

function getSiteDomain(host) {
  return host.replace(/^www\./, '').toLowerCase();
}

function isSiteInList(domain, list) {
  var d = domain.toLowerCase();
  for (var i = 0; i < list.length; i++) {
    var allowed = list[i].toLowerCase();
    if (d === allowed || d.endsWith('.' + allowed)) return true;
  }
  return false;
}

document.addEventListener('DOMContentLoaded', function() {
  // Get current tab domain first
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    if (tabs && tabs[0] && tabs[0].url) {
      try {
        var url = new URL(tabs[0].url);
        currentTabDomain = getSiteDomain(url.hostname);
        document.getElementById('current-site-domain').textContent = currentTabDomain;
      } catch(e) {
        currentTabDomain = '';
        document.getElementById('current-site-domain').textContent = '(unknown)';
      }
    }

    // Load settings
    chrome.storage.local.get(['serverUrl', 'apiKey', 'allowedSites', 'blockedSites'], function(result) {
      var url = (result.serverUrl || SERVER_URL).replace(/\/+$/, '');
      document.getElementById('server-url').value = url;
      SERVER_URL = url;

      if (result.apiKey) {
        API_KEY = result.apiKey;
        document.getElementById('api-key').value = result.apiKey;
      }

      allowedSites = (result.allowedSites && Array.isArray(result.allowedSites))
        ? result.allowedSites.slice().sort() : [];
      blockedSites = (result.blockedSites && Array.isArray(result.blockedSites))
        ? result.blockedSites.slice().sort() : [];

      updateSiteActivationUI();
      renderSiteList();
      renderDismissedList();

      checkServerStatus();
      loadStats();
      checkSchemaStatus();
    });
  });

  document.getElementById('server-url').addEventListener('change', function() {
    var url = (this.value.trim() || 'http://localhost:7046').replace(/\/+$/, '');
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

  // Activate/deactivate current site
  document.getElementById('activate-btn').addEventListener('click', function() {
    if (!currentTabDomain) return;
    if (!isSiteInList(currentTabDomain, allowedSites)) {
      allowedSites.push(currentTabDomain);
      allowedSites.sort();
    }
    // Remove from dismissed list if it was there
    blockedSites = blockedSites.filter(function(s) {
      return s.toLowerCase() !== currentTabDomain.toLowerCase();
    });
    saveSiteLists(function() {
      updateSiteActivationUI();
      renderSiteList();
      renderDismissedList();
      showMessage('Activated ' + currentTabDomain + '. Reload the page to start extracting.');
      notifyContentScript('activateSite');
    });
  });

  document.getElementById('deactivate-btn').addEventListener('click', function() {
    if (!currentTabDomain) return;
    allowedSites = allowedSites.filter(function(s) {
      return s.toLowerCase() !== currentTabDomain.toLowerCase();
    });
    saveSiteLists(function() {
      updateSiteActivationUI();
      renderSiteList();
      showMessage('Deactivated ' + currentTabDomain + '. Reload the page to stop.');
    });
  });

  // Toggle allowed sites list
  document.getElementById('sites-toggle').addEventListener('click', function() {
    var content = document.getElementById('sites-content');
    var expanded = content.classList.toggle('expanded');
    this.innerHTML = expanded ? '&#9650; Hide' : '&#9660; Show';
  });

  // Toggle dismissed sites list
  document.getElementById('dismissed-toggle').addEventListener('click', function() {
    var content = document.getElementById('dismissed-content');
    var expanded = content.classList.toggle('expanded');
    this.innerHTML = expanded ? '&#9650; Hide' : '&#9660; Show';
  });

  // Allowed sites: edit as text toggle
  document.getElementById('sites-edit-toggle').addEventListener('click', function() {
    var area = document.getElementById('sites-edit-area');
    area.style.display = area.style.display === 'none' ? 'block' : 'none';
    if (area.style.display === 'block') {
      document.getElementById('sites-textarea').value = allowedSites.join('\n');
    }
  });

  document.getElementById('sites-save').addEventListener('click', function() {
    var text = document.getElementById('sites-textarea').value;
    allowedSites = text.split('\n')
      .map(function(s) { return s.trim().toLowerCase().replace(/^https?:\/\//, '').replace(/^www\./, '').replace(/\/.*$/, ''); })
      .filter(function(s) { return s.length > 0; });
    allowedSites.sort();
    // Deduplicate
    allowedSites = allowedSites.filter(function(s, i, a) { return i === 0 || s !== a[i - 1]; });
    saveSiteLists(function() {
      renderSiteList();
      updateSiteActivationUI();
      document.getElementById('sites-edit-area').style.display = 'none';
      showMessage('Activated sites saved (' + allowedSites.length + '). Reload tabs for changes to take effect.');
    });
  });

  document.getElementById('sites-edit-cancel').addEventListener('click', function() {
    document.getElementById('sites-edit-area').style.display = 'none';
  });

  // Dismissed sites: edit as text toggle
  document.getElementById('dismissed-edit-toggle').addEventListener('click', function() {
    var area = document.getElementById('dismissed-edit-area');
    area.style.display = area.style.display === 'none' ? 'block' : 'none';
    if (area.style.display === 'block') {
      document.getElementById('dismissed-textarea').value = blockedSites.join('\n');
    }
  });

  document.getElementById('dismissed-save').addEventListener('click', function() {
    var text = document.getElementById('dismissed-textarea').value;
    blockedSites = text.split('\n')
      .map(function(s) { return s.trim().toLowerCase().replace(/^https?:\/\//, '').replace(/^www\./, '').replace(/\/.*$/, ''); })
      .filter(function(s) { return s.length > 0; });
    blockedSites.sort();
    blockedSites = blockedSites.filter(function(s, i, a) { return i === 0 || s !== a[i - 1]; });
    saveSiteLists(function() {
      renderDismissedList();
      updateSiteActivationUI();
      document.getElementById('dismissed-edit-area').style.display = 'none';
      showMessage('Dismissed sites saved (' + blockedSites.length + '). Reload tabs for changes to take effect.');
    });
  });

  document.getElementById('dismissed-edit-cancel').addEventListener('click', function() {
    document.getElementById('dismissed-edit-area').style.display = 'none';
  });

  // Add site manually
  document.getElementById('add-site-btn').addEventListener('click', addSiteManually);
  document.getElementById('add-site-input').addEventListener('keydown', function(e) {
    if (e.key === 'Enter') addSiteManually();
  });
});

function addSiteManually() {
  var input = document.getElementById('add-site-input');
  var domain = input.value.trim().toLowerCase().replace(/^https?:\/\//, '').replace(/^www\./, '').replace(/\/.*$/, '');
  if (!domain || domain.length < 3) return;

  if (isSiteInList(domain, allowedSites)) {
    showMessage(domain + ' is already activated.');
    return;
  }

  allowedSites.push(domain);
  allowedSites.sort();
  // Remove from dismissed if it was there
  blockedSites = blockedSites.filter(function(s) {
    return s.toLowerCase() !== domain.toLowerCase();
  });
  input.value = '';

  saveSiteLists(function() {
    updateSiteActivationUI();
    renderSiteList();
    renderDismissedList();
    showMessage('Added ' + domain);
  });
}

function removeSite(domain) {
  allowedSites = allowedSites.filter(function(s) {
    return s.toLowerCase() !== domain.toLowerCase();
  });
  saveSiteLists(function() {
    updateSiteActivationUI();
    renderSiteList();
  });
}

function undismissSite(domain) {
  blockedSites = blockedSites.filter(function(s) {
    return s.toLowerCase() !== domain.toLowerCase();
  });
  saveSiteLists(function() {
    updateSiteActivationUI();
    renderDismissedList();
  });
}

function saveSiteLists(callback) {
  chrome.storage.local.set({ allowedSites: allowedSites, blockedSites: blockedSites }, callback || function() {});
}

function updateSiteActivationUI() {
  var dot = document.getElementById('site-status-dot');
  var text = document.getElementById('site-status-text');
  var activateBtn = document.getElementById('activate-btn');
  var deactivateBtn = document.getElementById('deactivate-btn');
  var countEl = document.getElementById('sites-count');

  countEl.textContent = allowedSites.length;

  if (!currentTabDomain) {
    dot.className = 'status-dot';
    text.textContent = 'No site detected';
    activateBtn.style.display = 'none';
    deactivateBtn.style.display = 'none';
    return;
  }

  if (isSiteInList(currentTabDomain, allowedSites)) {
    dot.className = 'status-dot active';
    text.textContent = 'Active';
    activateBtn.style.display = 'none';
    deactivateBtn.style.display = '';
  } else {
    dot.className = 'status-dot inactive';
    text.textContent = 'Not activated';
    activateBtn.style.display = '';
    deactivateBtn.style.display = 'none';
  }
}

function renderSiteList() {
  var listEl = document.getElementById('site-list');
  if (allowedSites.length === 0) {
    listEl.innerHTML = '<div class="empty-list">No sites activated yet.<br>Visit a job site and click Activate.</div>';
    return;
  }

  listEl.innerHTML = '';
  allowedSites.forEach(function(site) {
    var item = document.createElement('div');
    item.className = 'site-item';

    var name = document.createElement('span');
    name.className = 'site-name';
    name.textContent = site;

    var removeBtn = document.createElement('button');
    removeBtn.className = 'site-remove-btn';
    removeBtn.textContent = 'Remove';
    removeBtn.onclick = function() { removeSite(site); };

    item.appendChild(name);
    item.appendChild(removeBtn);
    listEl.appendChild(item);
  });
}

function renderDismissedList() {
  var countEl = document.getElementById('dismissed-count');
  var listEl = document.getElementById('dismissed-list');

  countEl.textContent = blockedSites.length;

  if (blockedSites.length === 0) {
    listEl.innerHTML = '<div class="empty-list">No dismissed sites.</div>';
    return;
  }

  listEl.innerHTML = '';
  blockedSites.forEach(function(site) {
    var item = document.createElement('div');
    item.className = 'site-item';

    var name = document.createElement('span');
    name.className = 'site-name';
    name.textContent = site;

    var removeBtn = document.createElement('button');
    removeBtn.className = 'site-remove-btn';
    removeBtn.textContent = 'Unblock';
    removeBtn.onclick = function() { undismissSite(site); };

    item.appendChild(name);
    item.appendChild(removeBtn);
    listEl.appendChild(item);
  });
}

function notifyContentScript(action) {
  chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    if (tabs && tabs[0] && tabs[0].id) {
      chrome.tabs.sendMessage(tabs[0].id, { action: action }, function() {
        // Ignore errors (content script may not be ready)
        if (chrome.runtime.lastError) {}
      });
    }
  });
}

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
