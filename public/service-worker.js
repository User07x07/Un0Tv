/**
 * Service Worker for UN0 Movie Streaming Platform
 * Implements two-tier caching strategy for optimal performance on Android TV
 * 
 * Cache Strategy:
 * - Cache-first for: static assets (JS, CSS, fonts), movie posters, previews, backdrops
 * - Network-first for: dynamic content, API responses, authentication
 */

const CACHE_VERSION = 'v1.0.0';
const STATIC_CACHE = `static-${CACHE_VERSION}`;
const IMAGE_CACHE = `images-${CACHE_VERSION}`;
const API_CACHE = `api-${CACHE_VERSION}`;

// Assets to cache on install
const STATIC_ASSETS = [
  '/',
  '/index.html',
  '/css/styles.css',
  '/js/app.js',
  '/js/utils.js',
  '/manifest.json'
];

// Install event: cache static assets
self.addEventListener('install', event => {
  console.log('[ServiceWorker] Installing version:', CACHE_VERSION);

  event.waitUntil(
    caches.open(STATIC_CACHE)
      .then(cache => {
        console.log('[ServiceWorker] Caching static assets');
        return cache.addAll(STATIC_ASSETS);
      })
      .then(() => self.skipWaiting())
      .catch(err => console.error('[ServiceWorker] Install error:', err))
  );
});

// Activate event: clean up old caches
self.addEventListener('activate', event => {
  console.log('[ServiceWorker] Activating');

  event.waitUntil(
    caches.keys()
      .then(cacheNames => {
        return Promise.all(
          cacheNames
            .filter(name => {
              // Delete old versions of all caches
              return (name.startsWith('static-') && name !== STATIC_CACHE) ||
                     (name.startsWith('images-') && name !== IMAGE_CACHE) ||
                     (name.startsWith('api-') && name !== API_CACHE);
            })
            .map(name => {
              console.log('[ServiceWorker] Deleting old cache:', name);
              return caches.delete(name);
            })
        );
      })
      .then(() => self.clients.claim())
      .catch(err => console.error('[ServiceWorker] Activate error:', err))
  );
});

// Fetch event: implement caching strategies
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);

  // Skip non-GET requests
  if (event.request.method !== 'GET') {
    return;
  }

  // Skip chrome extensions and other non-http(s) protocols
  if (!url.protocol.startsWith('http')) {
    return;
  }

  // 1. CACHE-FIRST STRATEGY for static assets, images, and movie previews
  if (isStaticAsset(url.pathname) || isMoviePreview(url)) {
    event.respondWith(
      caches.open(IMAGE_CACHE)
        .then(cache => {
          return cache.match(event.request)
            .then(response => {
              // Return cached version if available
              if (response) {
                console.log('[ServiceWorker] Serving from cache (static/preview):', url.pathname);
                return response;
              }

              // Otherwise, fetch from network and cache it
              return fetch(event.request)
                .then(response => {
                  // Only cache successful responses
                  if (!response || response.status !== 200 || response.type === 'error') {
                    return response;
                  }

                  // Clone the response before caching
                  const responseToCache = response.clone();
                  cache.put(event.request, responseToCache);
                  console.log('[ServiceWorker] Cached from network:', url.pathname);
                  return response;
                })
                .catch(err => {
                  console.warn('[ServiceWorker] Fetch failed for:', url.pathname, err);
                  // Return cached version if available
                  return cache.match(event.request)
                    .then(cachedResponse => cachedResponse || createOfflineResponse());
                });
            });
        })
        .catch(err => {
          console.error('[ServiceWorker] Cache error:', err);
          return fetch(event.request);
        })
    );
  }

  // 2. NETWORK-FIRST STRATEGY for API calls and dynamic content
  if (isApiUrl(url)) {
    event.respondWith(
      fetch(event.request)
        .then(response => {
          // Cache successful API responses
          if (response && response.status === 200) {
            const responseToCache = response.clone();
            caches.open(API_CACHE)
              .then(cache => cache.put(event.request, responseToCache));
          }
          return response;
        })
        .catch(err => {
          console.warn('[ServiceWorker] Network request failed:', url.href, err);
          // Fall back to cached API response if available
          return caches.open(API_CACHE)
            .then(cache => cache.match(event.request))
            .then(response => response || createOfflineResponse());
        })
    );
  }

  // 3. DEFAULT: Network-first for other content
  event.respondWith(
    fetch(event.request)
      .then(response => {
        if (!response || response.status !== 200) {
          return response;
        }

        // Cache successful responses
        const responseToCache = response.clone();
        caches.open(STATIC_CACHE)
          .then(cache => cache.put(event.request, responseToCache));

        return response;
      })
      .catch(err => {
        console.warn('[ServiceWorker] Default fetch failed:', url.href, err);
        return caches.match(event.request)
          .then(response => response || createOfflineResponse());
      })
  );
});

/**
 * Helper: Check if URL is a static asset
 * Includes CSS, JS, fonts, and static images
 */
function isStaticAsset(pathname) {
  return /\.(js|css|woff|woff2|ttf|eot|svg)$/.test(pathname);
}

/**
 * Helper: Check if URL is a movie preview/poster/backdrop image
 * Matches TMDB image URLs, poster files, and preview images
 */
function isMoviePreview(url) {
  const pathname = url.pathname.toLowerCase();
  const hostname = url.hostname.toLowerCase();

  // Match TMDB image domains
  if (hostname.includes('image.tmdb.org') || hostname.includes('tmdb.org')) {
    return true;
  }

  // Match URL patterns for movie previews
  if (pathname.includes('poster') ||
      pathname.includes('backdrop') ||
      pathname.includes('preview') ||
      pathname.includes('thumb')) {
    return true;
  }

  // Match image file extensions used for movie media
  if (/\.(jpg|jpeg|png|webp|gif)(\?.*)?$/.test(pathname)) {
    return true;
  }

  return false;
}

/**
 * Helper: Check if URL is an API endpoint
 * Typically includes /api/ in path or specific data endpoints
 */
function isApiUrl(url) {
  return url.pathname.includes('/api/') ||
         url.pathname.includes('/data/') ||
         url.pathname.includes('/search') ||
         url.pathname.includes('/discover');
}

/**
 * Helper: Create offline response when network and cache unavailable
 */
function createOfflineResponse() {
  const html = `
    <!DOCTYPE html>
    <html>
    <head>
      <title>Offline</title>
      <style>
        body {
          font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
          display: flex;
          justify-content: center;
          align-items: center;
          height: 100vh;
          margin: 0;
          background: #1a1a1a;
          color: #fff;
        }
        .container {
          text-align: center;
          padding: 2rem;
        }
        h1 { font-size: 2rem; margin-bottom: 1rem; }
        p { font-size: 1.1rem; color: #aaa; }
      </style>
    </head>
    <body>
      <div class="container">
        <h1>📡 You are offline</h1>
        <p>Unable to load the requested content.</p>
        <p>Please check your internet connection and try again.</p>
      </div>
    </body>
    </html>
  `;

  return new Response(html, {
    status: 503,
    statusText: 'Service Unavailable',
    headers: new Headers({ 'Content-Type': 'text/html' })
  });
}

// Message handler for cache management from clients
self.addEventListener('message', event => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }

  if (event.data && event.data.type === 'CLEAR_CACHE') {
    caches.keys()
      .then(cacheNames => {
        return Promise.all(
          cacheNames.map(name => {
            console.log('[ServiceWorker] Clearing cache:', name);
            return caches.delete(name);
          })
        );
      })
      .then(() => {
        event.ports[0].postMessage({ success: true });
      })
      .catch(err => {
        console.error('[ServiceWorker] Error clearing cache:', err);
        event.ports[0].postMessage({ success: false, error: err.message });
      });
  }
});

console.log('[ServiceWorker] Loaded successfully');
