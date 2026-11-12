/**
 * Service Worker Registration and Management
 * This script should be included in your main HTML file (e.g., index.html)
 * 
 * Handles:
 * - Service Worker registration
 * - Update notifications
 * - Cache management
 * - Performance monitoring
 */

(function initServiceWorker() {
  'use strict';

  // Check if browser supports Service Workers
  if (!('serviceWorker' in navigator)) {
    console.log('[SW Manager] Service Workers not supported in this browser');
    return;
  }

  // Configuration
  const SW_SCRIPT_PATH = '/service-worker.js';
  const CHECK_UPDATE_INTERVAL = 60 * 60 * 1000; // 1 hour in milliseconds
  const LOCAL_STORAGE_KEY = 'sw_install_time';

  /**
   * Register Service Worker
   */
  function registerServiceWorker() {
    navigator.serviceWorker
      .register(SW_SCRIPT_PATH, { scope: '/' })
      .then(registration => {
        console.log('[SW Manager] Registration successful, scope:', registration.scope);

        // Store installation time
        localStorage.setItem(LOCAL_STORAGE_KEY, new Date().toISOString());

        // Listen for updates
        registration.addEventListener('updatefound', onUpdateFound);

        // Check for updates periodically
        setInterval(() => {
          registration.update();
        }, CHECK_UPDATE_INTERVAL);

        // Notify user about cache status
        updateCacheStatus();
      })
      .catch(error => {
        console.error('[SW Manager] Registration failed:', error);
      });
  }

  /**
   * Handle when a new Service Worker version is found
   */
  function onUpdateFound(event) {
    const registration = event.target;
    const newWorker = registration.installing;

    newWorker.addEventListener('statechange', () => {
      if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
        // New service worker available
        console.log('[SW Manager] New version available');
        notifyUserAboutUpdate();
      }
    });
  }

  /**
   * Notify user that an update is available
   * This allows users to skip waiting and get the new version immediately
   */
  function notifyUserAboutUpdate() {
    const updateAvailable = confirm(
      'A new version of UN0 is available. Would you like to update now?'
    );

    if (updateAvailable) {
      // Send message to service worker to skip waiting
      if (navigator.serviceWorker.controller) {
        navigator.serviceWorker.controller.postMessage({ type: 'SKIP_WAITING' });

        // Reload page after brief delay to allow new worker to activate
        setTimeout(() => {
          window.location.reload();
        }, 500);
      }
    }
  }

  /**
   * Update cache status display
   * Provides user feedback about cached content
   */
  function updateCacheStatus() {
    caches.keys()
      .then(cacheNames => {
        let totalSize = 0;
        let imageCount = 0;

        return Promise.all(
          cacheNames.map(cacheName => {
            return caches.open(cacheName)
              .then(cache => {
                return cache.keys()
                  .then(requests => {
                    requests.forEach(request => {
                      if (request.url.includes('.jpg') ||
                          request.url.includes('.png') ||
                          request.url.includes('.gif') ||
                          request.url.includes('.webp') ||
                          request.url.includes('poster') ||
                          request.url.includes('backdrop')) {
                        imageCount++;
                      }
                    });
                    return requests.length;
                  });
              });
          })
        ).then(results => {
          const totalItems = results.reduce((sum, count) => sum + count, 0);
          console.log(
            `[SW Manager] Cache Status: ${totalItems} items cached, ${imageCount} images`
          );

          // Optionally update UI element if it exists
          const cacheStatusElement = document.getElementById('cache-status');
          if (cacheStatusElement) {
            cacheStatusElement.textContent = 
              `📦 ${totalItems} items cached (${imageCount} images)`;
          }

          // Dispatch custom event for other scripts to listen to
          window.dispatchEvent(new CustomEvent('cacheStatusUpdated', {
            detail: { totalItems, imageCount }
          }));
        });
      })
      .catch(error => {
        console.error('[SW Manager] Error getting cache status:', error);
      });
  }

  /**
   * Clear all caches
   * Allows users to manually clear cached content
   */
  window.clearAllCaches = function() {
    return caches.keys()
      .then(cacheNames => {
        return Promise.all(
          cacheNames.map(cacheName => {
            console.log('[SW Manager] Clearing cache:', cacheName);
            return caches.delete(cacheName);
          })
        );
      })
      .then(() => {
        console.log('[SW Manager] All caches cleared');

        // Send message to service worker
        if (navigator.serviceWorker.controller) {
          navigator.serviceWorker.controller.postMessage({ type: 'CLEAR_CACHE' });
        }

        // Update status display
        updateCacheStatus();
        return true;
      })
      .catch(error => {
        console.error('[SW Manager] Error clearing caches:', error);
        return false;
      });
  };

  /**
   * Get cache statistics
   * Returns information about current cache usage
   */
  window.getCacheStats = async function() {
    const stats = {
      totalSize: 0,
      totalItems: 0,
      caches: {}
    };

    const cacheNames = await caches.keys();

    for (const cacheName of cacheNames) {
      const cache = await caches.open(cacheName);
      const requests = await cache.keys();

      stats.caches[cacheName] = {
        itemCount: requests.length,
        urls: requests.map(r => r.url)
      };

      stats.totalItems += requests.length;
    }

    return stats;
  };

  /**
   * Monitor connection speed and adjust caching strategy
   * Useful for adaptive loading on slow connections
   */
  if ('connection' in navigator) {
    navigator.connection.addEventListener('change', () => {
      const effectiveType = navigator.connection.effectiveType;
      console.log('[SW Manager] Connection type changed:', effectiveType);

      // Dispatch event for other components to react to connection changes
      window.dispatchEvent(new CustomEvent('connectionTypeChanged', {
        detail: { effectiveType }
      }));
    });
  }

  /**
   * Periodic cache cleanup
   * Remove stale cached data older than 30 days
   */
  function initPeriodicCleanup() {
    setInterval(async () => {
      const installTime = localStorage.getItem(LOCAL_STORAGE_KEY);
      if (!installTime) return;

      const daysSinceInstall = 
        (new Date() - new Date(installTime)) / (1000 * 60 * 60 * 24);

      if (daysSinceInstall > 30) {
        console.log('[SW Manager] Triggering periodic cleanup (>30 days)');
        window.clearAllCaches();
        localStorage.setItem(LOCAL_STORAGE_KEY, new Date().toISOString());
      }
    }, 24 * 60 * 60 * 1000); // Check once per day
  }

  /**
   * Initialize
   */
  function init() {
    registerServiceWorker();
    initPeriodicCleanup();

    // Provide public API for debugging
    window.serviceWorkerManager = {
      getStats: window.getCacheStats,
      clearCache: window.clearAllCaches,
      checkUpdate: () => {
        navigator.serviceWorker.ready.then(reg => reg.update());
      }
    };

    console.log('[SW Manager] Initialized. Access manager with: window.serviceWorkerManager');
  }

  // Initialize when DOM is ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

})();
