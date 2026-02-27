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
