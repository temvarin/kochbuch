// Offline support: the service worker keeps a copy of the app (service-worker.published.js). Registered here rather
// than in an inline script in index.html, which the gateway's Content Security Policy doesn't allow.
navigator.serviceWorker?.register('service-worker.js', { updateViaCache: 'none' });

// Browser features the app calls from .NET (Shell/Browser.cs). Ported from kassensturz.
window.koch = {
  // Browsers may delete a site's storage when the device runs low on space, unless the site is marked persistent.
  async protectStorage() {
    if (!navigator.storage?.persist) return false;
    try {
      return await navigator.storage.persisted() || await navigator.storage.persist();
    } catch {
      return false;
    }
  },

  // Also when the user returns to the installed app, which the phone kept in memory instead of starting it anew.
  whenVisible(browser) {
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') browser.invokeMethodAsync('OnVisible');
    });
  },
};
