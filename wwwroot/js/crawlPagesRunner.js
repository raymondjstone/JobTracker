window.crawlPagesRunner = {
  openAndScheduleClose: function (url, delaySeconds) {
    const seconds = Math.max(0, Number(delaySeconds) || 0);
    const w = window.open(url, '_blank');
    if (!w) return false;

    window.setTimeout(() => {
      try { w.close(); } catch { }
    }, seconds * 1000);

    return true;
  }
};

window.jobTracker = {
  shutdown: function () {
    document.title = 'JobTracker - Shut Down';
    document.body.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100vh;font-family:system-ui;color:#666;"><div style="text-align:center;"><h2>JobTracker has shut down</h2><p>You can close this tab.</p></div></div>';
    fetch('/api/shutdown', { method: 'POST' });
    setTimeout(function () { window.close(); }, 200);
  }
};
