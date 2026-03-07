// Background service worker - proxies API calls to avoid CORS issues.
// Content scripts run in the page's origin (e.g. monster.com), which isn't
// in the server's CORS whitelist. The background script runs in the
// chrome-extension:// origin, which IS allowed.

chrome.runtime.onMessage.addListener(function(message, sender, sendResponse) {
  if (message.type === 'api') {
    doFetch(message.url, message.options)
      .then(function(result) { sendResponse(result); })
      .catch(function(err) { sendResponse({ error: err.message }); });
    return true; // keep channel open for async response
  }
});

async function doFetch(url, options) {
  var response = await fetch(url, options);
  var text = await response.text();
  var data = null;
  try { data = JSON.parse(text); } catch(e) { data = text; }
  return { ok: response.ok, status: response.status, data: data };
}
