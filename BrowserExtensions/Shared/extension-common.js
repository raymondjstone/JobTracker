/**
 * Shared utility module for JobTracker browser extensions.
 *
 * Usage: Copy this file into your extension directory or reference it
 * from manifest.json content_scripts.
 *
 * Provides:
 *  - Settings loading (server URL, API key)
 *  - HTTP headers with API key
 *  - Job sending to server API
 *  - UI creation (floating status badge)
 *  - Auto-fetch UI (progress overlay)
 *  - URL normalization
 *  - Text cleaning utilities
 */

var JobTrackerCommon = (function() {
  'use strict';

  var SERVER_URL = 'http://localhost:7046';
  var API_KEY = '';
  var _prefix = 'jt'; // UI element prefix, set per-extension
  var _paused = false;
  var _pauseStorageKey = 'paused_' + window.location.hostname;

  /**
   * Initialize common settings from chrome.storage.
   * @param {string} prefix - Short prefix for UI elements (e.g., 'lje', 'ind')
   * @param {function} [callback] - Called after settings are loaded
   */
  function init(prefix, callback) {
    _prefix = prefix || 'jt';
    _pauseStorageKey = 'paused_' + window.location.hostname;
    if (typeof chrome !== 'undefined' && chrome.storage && chrome.storage.local) {
      chrome.storage.local.get(['serverUrl', 'apiKey', _pauseStorageKey], function(result) {
        if (result.serverUrl) {
          SERVER_URL = result.serverUrl.replace(/\/+$/, '');
        }
        if (result.apiKey) {
          API_KEY = result.apiKey;
        }
        if (result[_pauseStorageKey]) {
          _paused = true;
          updatePauseUI();
        }
        if (callback) callback();
      });
    }
  }

  /**
   * Returns HTTP headers with Content-Type and API key.
   * @returns {Object} Headers object
   */
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

  /**
   * Get the current server URL.
   * @returns {string}
   */
  function getServerUrl() {
    return SERVER_URL;
  }

  /**
   * Send a job listing to the server.
   * @param {Object} job - Job data { title, company, url, source, location, salary, description, jobType }
   * @param {function} onSuccess - Called with response data on success
   * @param {function} onDuplicate - Called when job already exists
   * @param {function} onError - Called with error message on failure
   */
  function sendJob(job, onSuccess, onDuplicate, onError) {
    fetch(SERVER_URL + '/api/jobs', {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify(job)
    })
    .then(function(response) { return response.json(); })
    .then(function(data) {
      if (data.added) {
        if (onSuccess) onSuccess(data);
      } else {
        if (onDuplicate) onDuplicate(data);
      }
    })
    .catch(function(error) {
      if (onError) onError(error.message || 'Network error');
    });
  }

  /**
   * Check if a job URL already exists on the server.
   * @param {string} url - Job URL to check
   * @param {function} callback - Called with boolean (true if exists)
   */
  function checkJobExists(url, callback) {
    fetch(SERVER_URL + '/api/jobs/exists?url=' + encodeURIComponent(url), {
      headers: getHeaders()
    })
    .then(function(r) { return r.json(); })
    .then(function(data) { callback(data.exists); })
    .catch(function() { callback(false); });
  }

  /**
   * Update a job's description on the server.
   * @param {Object} data - { url, description, company?, contacts?, jobType? }
   * @param {function} [onSuccess]
   * @param {function} [onError]
   */
  function updateDescription(data, onSuccess, onError) {
    fetch(SERVER_URL + '/api/jobs/description', {
      method: 'PUT',
      headers: getHeaders(),
      body: JSON.stringify(data)
    })
    .then(function(r) { return r.json(); })
    .then(function(result) { if (onSuccess) onSuccess(result); })
    .catch(function(err) { if (onError) onError(err.message); });
  }

  /**
   * Fetch list of jobs that need descriptions from the server.
   * @param {number} [limit] - Max jobs to return
   * @param {function} callback - Called with { count, jobs: [{ url, title, company, source }] }
   */
  function getJobsNeedingDescriptions(limit, callback) {
    var url = SERVER_URL + '/api/jobs/needing-descriptions';
    if (limit) url += '?limit=' + limit;
    fetch(url, { headers: getHeaders() })
      .then(function(r) { return r.json(); })
      .then(callback)
      .catch(function() { callback({ count: 0, jobs: [] }); });
  }

  // === UI Utilities ===

  /**
   * Create the floating status badge.
   * @param {string} color - Background color (e.g., '#0a66c2')
   * @param {function} onClick - Click handler
   */
  function createBadge(color, onClick) {
    if (document.getElementById(_prefix + '-ui')) return;
    var el = document.createElement('div');
    el.id = _prefix + '-ui';
    el.innerHTML = '<span id="' + _prefix + '-dot"></span>' +
                   '<span id="' + _prefix + '-text">Ready</span>' +
                   '<span id="' + _prefix + '-count">0</span>' +
                   '<span id="' + _prefix + '-pause" title="Pause/Resume">&#10074;&#10074;</span>';
    el.style.cssText = 'position:fixed;bottom:20px;right:20px;background:' + color + ';color:#fff;padding:8px 16px;border-radius:20px;font:bold 12px Arial;z-index:2147483647;display:flex;align-items:center;gap:8px;box-shadow:0 4px 15px rgba(0,0,0,0.3);cursor:pointer;';
    el.querySelector('#' + _prefix + '-dot').style.cssText = 'width:10px;height:10px;background:#4ade80;border-radius:50%;';
    el.querySelector('#' + _prefix + '-count').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;';
    el.querySelector('#' + _prefix + '-pause').style.cssText = 'background:rgba(255,255,255,0.2);padding:2px 8px;border-radius:10px;font-size:10px;letter-spacing:-2px;';
    el.querySelector('#' + _prefix + '-pause').onclick = function(e) { e.stopPropagation(); togglePause(); };
    document.body.appendChild(el);
    if (onClick) el.onclick = onClick;
    if (_paused) updatePauseUI();
  }

  /**
   * Update the badge text and count.
   * @param {string} text - Status text
   * @param {number|string} count - Count to display
   */
  function updateBadge(text, count) {
    if (_paused) return;
    var t = document.getElementById(_prefix + '-text');
    var c = document.getElementById(_prefix + '-count');
    if (t) t.textContent = text;
    if (c) c.textContent = count;
  }

  /**
   * Toggle the pause state for this hostname.
   */
  function togglePause() {
    _paused = !_paused;
    var data = {};
    data[_pauseStorageKey] = _paused ? true : false;
    chrome.storage.local.set(data);
    updatePauseUI();
  }

  /**
   * Update the badge UI to reflect pause state.
   */
  function updatePauseUI() {
    var dot = document.getElementById(_prefix + '-dot');
    var text = document.getElementById(_prefix + '-text');
    var btn = document.getElementById(_prefix + '-pause');
    var container = document.getElementById(_prefix + '-ui');
    if (_paused) {
      if (dot) dot.style.background = '#f87171';
      if (text) text.textContent = 'Paused';
      if (btn) btn.innerHTML = '&#9654;';
      if (container) container.style.opacity = '0.7';
    } else {
      if (dot) dot.style.background = '#4ade80';
      if (text) text.textContent = 'Ready';
      if (btn) btn.innerHTML = '&#10074;&#10074;';
      if (container) container.style.opacity = '1';
    }
  }

  /**
   * Check if the extension is currently paused.
   * @returns {boolean}
   */
  function isPaused() {
    return _paused;
  }

  /**
   * Show the auto-fetch progress overlay.
   * @param {string} color - Background color
   * @param {function} onStop - Stop button handler
   */
  function showAutoFetchUI(color, onStop) {
    var existing = document.getElementById(_prefix + '-autofetch-ui');
    if (existing) existing.remove();

    var el = document.createElement('div');
    el.id = _prefix + '-autofetch-ui';
    el.innerHTML = '<div id="' + _prefix + '-af-status">Auto-fetching descriptions...</div>' +
                   '<div id="' + _prefix + '-af-progress">Job <span id="' + _prefix + '-af-current">0</span> of <span id="' + _prefix + '-af-total">0</span></div>' +
                   '<div id="' + _prefix + '-af-job"></div>' +
                   '<button id="' + _prefix + '-af-stop">Stop</button>';
    el.style.cssText = 'position:fixed;top:20px;right:20px;background:' + color + ';color:#fff;padding:16px 20px;border-radius:12px;font:12px Arial;z-index:2147483647;box-shadow:0 4px 20px rgba(0,0,0,0.4);min-width:280px;';
    el.querySelector('#' + _prefix + '-af-status').style.cssText = 'font-weight:bold;font-size:14px;margin-bottom:8px;';
    el.querySelector('#' + _prefix + '-af-progress').style.cssText = 'margin-bottom:8px;';
    el.querySelector('#' + _prefix + '-af-job').style.cssText = 'font-size:11px;opacity:0.8;margin-bottom:12px;max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
    el.querySelector('#' + _prefix + '-af-stop').style.cssText = 'background:rgba(0,0,0,0.3);border:none;color:#fff;padding:8px 16px;border-radius:6px;cursor:pointer;font-weight:bold;';
    el.querySelector('#' + _prefix + '-af-stop').onclick = onStop;
    document.body.appendChild(el);
  }

  /**
   * Update auto-fetch progress display.
   * @param {number} current
   * @param {number} total
   * @param {string} jobTitle
   */
  function updateAutoFetchUI(current, total, jobTitle) {
    var c = document.getElementById(_prefix + '-af-current');
    var t = document.getElementById(_prefix + '-af-total');
    var j = document.getElementById(_prefix + '-af-job');
    if (c) c.textContent = current;
    if (t) t.textContent = total;
    if (j) j.textContent = jobTitle || '';
  }

  /**
   * Remove the auto-fetch progress overlay.
   */
  function removeAutoFetchUI() {
    var el = document.getElementById(_prefix + '-autofetch-ui');
    if (el) el.remove();
  }

  // === Text & URL Utilities ===

  /**
   * Clean and normalize text (collapse whitespace, trim).
   * @param {string} text
   * @returns {string}
   */
  function cleanText(text) {
    return (text || '').replace(/\s+/g, ' ').trim();
  }

  /**
   * Normalize a job URL for deduplication.
   * Removes tracking params, fragments, trailing slashes.
   * @param {string} url
   * @returns {string}
   */
  function normalizeUrl(url) {
    if (!url) return '';
    try {
      var u = new URL(url);
      // Remove common tracking parameters
      var trackingParams = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_content', 'utm_term',
                           'fbclid', 'gclid', 'ref', 'refId', 'trackingId', 'trk', 'currentJobId',
                           'eBP', 'recommendedFlavor', 'origin', 'originalSubdomain', 'sk', 'rcChannel'];
      trackingParams.forEach(function(p) { u.searchParams.delete(p); });
      u.hash = '';
      var normalized = u.toString().replace(/\/+$/, '');
      return normalized;
    } catch (e) {
      return url.split('#')[0].split('?')[0].replace(/\/+$/, '');
    }
  }

  /**
   * Detect job source from URL hostname.
   * @param {string} url
   * @returns {string} Source name (e.g., 'LinkedIn', 'Indeed')
   */
  function detectSource(url) {
    if (!url) return 'Unknown';
    var host = '';
    try { host = new URL(url).hostname.toLowerCase(); } catch (e) { return 'Unknown'; }

    if (host.includes('linkedin.com')) return 'LinkedIn';
    if (host.includes('indeed.com')) return 'Indeed';
    if (host.includes('s1jobs.com')) return 'S1Jobs';
    if (host.includes('welcometothejungle')) return 'WTTJ';
    if (host.includes('energyjobsearch')) return 'EnergyJobSearch';
    if (host.includes('monster.com')) return 'Monster';
    if (host.includes('glassdoor.com')) return 'Glassdoor';
    if (host.includes('reed.co.uk')) return 'Reed';
    if (host.includes('totaljobs.com')) return 'TotalJobs';
    if (host.includes('cwjobs.co.uk')) return 'CWJobs';
    if (host.includes('jobserve.com')) return 'JobServe';
    if (host.includes('ziprecruiter')) return 'ZipRecruiter';
    if (host.includes('dice.com')) return 'Dice';
    if (host.includes('stackoverflow.com') || host.includes('stackoverflow.co')) return 'StackOverflow';
    if (host.includes('workday.com')) return 'Workday';
    if (host.includes('greenhouse.io')) return 'Greenhouse';
    if (host.includes('lever.co')) return 'Lever';
    return host.replace('www.', '').split('.')[0];
  }

  /**
   * Sanitize HTML to prevent XSS when inserting user data into DOM.
   * @param {string} str
   * @returns {string}
   */
  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
              .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  // Public API
  return {
    init: init,
    getHeaders: getHeaders,
    getServerUrl: getServerUrl,
    sendJob: sendJob,
    checkJobExists: checkJobExists,
    updateDescription: updateDescription,
    getJobsNeedingDescriptions: getJobsNeedingDescriptions,
    createBadge: createBadge,
    updateBadge: updateBadge,
    togglePause: togglePause,
    isPaused: isPaused,
    showAutoFetchUI: showAutoFetchUI,
    updateAutoFetchUI: updateAutoFetchUI,
    removeAutoFetchUI: removeAutoFetchUI,
    cleanText: cleanText,
    normalizeUrl: normalizeUrl,
    detectSource: detectSource,
    escapeHtml: escapeHtml
  };
})();
