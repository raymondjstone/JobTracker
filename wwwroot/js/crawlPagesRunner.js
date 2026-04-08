window.crawlPagesRunner = {
  _win: null,

  openAndScheduleClose: function (url, delaySeconds, isLast) {
    const seconds = Math.max(0, Number(delaySeconds) || 0);

    // Reuse existing window if still open, otherwise open a new one
    if (!this._win || this._win.closed) {
      this._win = window.open(url, '_blank');
      if (!this._win) return false;
      try { this._win.opener = null; } catch { }
    } else {
      this._win.location = url;
      try { this._win.focus(); } catch { }
    }

    // Close the window after the last page's delay
    if (isLast) {
      const w = this._win;
      const self = this;
      window.setTimeout(() => {
        try { w.close(); } catch { }
        self._win = null;
      }, seconds * 1000);
    }

    return true;
  }
};

window.jobTracker = {
  shutdown: function () {
    document.title = 'JobTracker - Shut Down';
    // Send shutdown request reliably — sendBeacon survives page unload; fetch+keepalive as fallback
    if (navigator.sendBeacon) {
      navigator.sendBeacon('/api/shutdown');
    } else {
      fetch('/api/shutdown', { method: 'POST', keepalive: true }).catch(function () { });
    }
    // Show goodbye message after request is dispatched
    document.body.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100vh;font-family:system-ui;color:#666;"><div style="text-align:center;"><h2>JobTracker has shut down</h2><p>You can close this tab.</p></div></div>';
    setTimeout(function () { window.close(); }, 500);
  },

  downloadFile: function (base64, mimeType, fileName) {
    var a = document.createElement('a');
    a.href = 'data:' + mimeType + ';base64,' + base64;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  },


  getLocalStorage: function (key) {
    return localStorage.getItem(key);
  },

  setLocalStorage: function (key, value) {
    localStorage.setItem(key, value);
  },

  hasAnyAttribute: function (attrNames) {
    for (var i = 0; i < attrNames.length; i++) {
      if (document.documentElement.hasAttribute(attrNames[i])) return true;
    }
    return false;
  }
};
