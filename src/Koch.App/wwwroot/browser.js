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

  // Keeps the screen on while cooking (Recipes/CookMode.razor). The browser drops the lock when the app goes to the
  // background, so it is taken again on return. Without the Screen Wake Lock API the screen sleeps as usual.
  async keepScreenOn() {
    screenLock.wanted = true;
    await screenLock.take();
  },

  async letScreenSleep() {
    screenLock.wanted = false;
    await screenLock.lock?.release().catch(() => {});
    screenLock.lock = null;
  },

  // Also when the user returns to the installed app, which the phone kept in memory instead of starting it anew.
  whenVisible(browser) {
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') browser.invokeMethodAsync('OnVisible');
    });
  },

  setLang(lang) {
    document.documentElement.lang = lang;
  },
};

const screenLock = {
  wanted: false,
  lock: null,
  async take() {
    if (!screenLock.wanted || screenLock.lock || !navigator.wakeLock || document.visibilityState !== 'visible') return;
    try {
      screenLock.lock = await navigator.wakeLock.request('screen');
      screenLock.lock.addEventListener('release', () => { screenLock.lock = null; });
    } catch {
      screenLock.lock = null; // e.g. battery saver: cooking works, the screen may just sleep
    }
  },
};
document.addEventListener('visibilitychange', () => screenLock.take());
