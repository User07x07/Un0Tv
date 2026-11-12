# UN0 Android TV – Caching Implementation Guide

## Overview

This guide documents the two-tier caching architecture implemented for the UN0 Android TV application to improve performance and reduce bandwidth consumption.

### Architecture Layers

#### Tier 1: Browser/Service Worker Caching (Primary)
- **Location**: Firebase website (movince-f5159.web.app)
- **Technology**: Service Worker + Cache API
- **Strategy**: Cache-first for versioned assets, network-first for dynamic content
- **Scope**: Static CSS, JavaScript, fonts, and images (movie previews, posters, backdrops) from CDNs

#### Tier 2: Native Android Cache Manager (Fallback)
- **Location**: App-private storage (/data/data/com.DevLink.Un0tv/files/image-cache)
- **Technology**: C# ImageCacheManager in Un0Tv/Services
- **Strategy**: Request interception in WebView
- **Scope**: Movie posters, preview thumbnails, backdrop images, and other media from whitelisted hosts

---

## Native Caching (Tier 2) - Already Implemented

### Components

#### 1. ImageCacheManager (`Un0Tv/Services/ImageCacheManager.cs`)

**Responsibilities:**
- Download images from network with 30-second timeout
- Cache images in app-private storage with SHA256-based filenames
- Track in-flight downloads to prevent duplicates
- Validate image content (magic bytes for JPEG, PNG, GIF, WebP, AVIF)
- Persist cache metadata as JSON
- Support 30-day cache expiration
- Asynchronous operations with CancellationToken support

**Key Methods:**
```csharp
// Get from cache or download
Task<CachedImage?> GetCachedImageAsync(string imageUrl, CancellationToken ct)

// Download and save to cache
Task<CachedImage?> DownloadImageAsync(string imageUrl, CancellationToken ct)

// Save image with metadata
Task<CachedImage?> SaveImageAsync(string imageUrl, byte[] data, CancellationToken ct)

// Remove expired images
Task<int> RemoveExpiredImagesAsync(CancellationToken ct)

// Clear all cache
Task<bool> ClearCacheAsync(CancellationToken ct)

// Get statistics
Task<CacheStatistics> GetStatisticsAsync(CancellationToken ct)

// Preload images
Task PreloadImagesAsync(IEnumerable<string> urls, CancellationToken ct)
```

#### 2. CacheStatisticsService (`Un0Tv/Services/CacheStatisticsService.cs`)

**Responsibilities:**
- Monitor cache size and image count
- Trigger automatic cleanup when limits exceeded
- Statistics caching with 5-minute TTL
- Cache clearing and management

**Configuration:**
```csharp
private const double MaxCacheSizeMB = 300;      // ~300 MB maximum
private const int MaxImageCount = 500;           // 500 images maximum
private TimeSpan StatisticsCacheDuration = TimeSpan.FromMinutes(5);
```

#### 3. CacheInterceptor (`Un0Tv/Platforms/Android/CacheInterceptor.cs`)

**Responsibilities:**
- Whitelist validation for cacheable hosts
- Integration point for WebView request interception
- Cache statistics retrieval
- Cache clearing API for UI

**Whitelisted Hosts:**
- themoviedb.org, image.tmdb.org, images.tmdb.org (Movie data, posters, previews, backdrops)
- cdn.jsdelivr.net, cdnjs.cloudflare.com (JS/CSS CDNs)
- cloudflare.com, akamaihd.net, fastly.net (Global CDNs)
- stackpath.bootstrapcdn.com, unpkg.com, googleapis.com, gstatic.com

### Cache Storage Structure

```
/data/data/com.DevLink.Un0tv/files/
├── image-cache/
│   ├── [2-char-prefix]/
│   │   ├── [SHA256-hash]      (image data)
│   │   └── ...
└── cache-metadata/
	├── [2-char-prefix]/
	│   ├── [SHA256-hash].json  (metadata)
	│   └── ...
```

**Metadata JSON Structure:**
```json
{
  "url": "https://image.tmdb.org/t/p/w342/poster.jpg",
  "cacheKey": "abc123def456...",
  "mimeType": "image/jpeg",
  "fileSize": 45678,
  "cachedAt": "2024-01-15T10:30:00Z",
  "lastAccessTime": "2024-01-15T14:22:00Z",
  "expiresAt": "2024-02-14T10:30:00Z"
}
```

### WebView Integration

**File**: `Un0Tv/Platforms/Android/TvWebViewClient.cs`

**Request Flow:**
1. `ShouldInterceptRequest()` receives HTTP request for an image
2. Check if URL is blocked (RequestFilter) → return empty response
3. Check if image is in native cache (CacheInterceptor with 100ms timeout)
4. If cached → serve from disk with proper MIME type
5. If not cached → proceed with normal network request
6. Every 10 page loads → trigger background cleanup

**Timeout Budget:**
- Cache lookup: 100 milliseconds
- Total: 200 milliseconds (measured with Task.Wait)
- UI thread preserved (non-blocking on timeout)

---

## Browser/Service Worker Caching (Tier 1) - Website Implementation

### Implementation for Website Owner

The UN0 dashboard website (hosted on Netlify) should implement a Service Worker for caching. This document provides the recommended structure.

#### Service Worker Location

**File**: `https://movince-f5159.web.app/service-worker.js`

**Registration** (in HTML):
```html
<script>
if ('serviceWorker' in navigator) {
	navigator.serviceWorker.register('/service-worker.js', {
		scope: '/'
	}).then(reg => {
		console.log('Service Worker registered:', reg);
	}).catch(err => {
		console.log('Service Worker registration failed:', err);
	});
}
</script>
```

#### Recommended Service Worker Implementation

```javascript
const CACHE_VERSION = 'v1.0.0'; // Update for new releases
const STATIC_CACHE = `static-${CACHE_VERSION}`;
const IMAGE_CACHE = `images-${CACHE_VERSION}`;
const API_CACHE = `api-${CACHE_VERSION}`;

// Assets to cache on installation
const STATIC_ASSETS = [
	'/',
	'/index.html',
	'/css/styles.css',
	'/js/app.js',
	'/js/vendor.js',
	'/fonts/opensans.ttf'
];

// Install event: pre-cache static assets
self.addEventListener('install', event => {
	event.waitUntil(
		caches.open(STATIC_CACHE).then(cache => {
			return cache.addAll(STATIC_ASSETS);
		}).then(() => self.skipWaiting())
	);
});

// Activate event: clean up old cache versions
self.addEventListener('activate', event => {
	event.waitUntil(
		caches.keys().then(cacheNames => {
			return Promise.all(
				cacheNames.map(name => {
					if (name !== STATIC_CACHE && 
						name !== IMAGE_CACHE && 
						name !== API_CACHE) {
						return caches.delete(name);
					}
				})
			);
		}).then(() => self.clients.claim())
	);
});

// Fetch event: implement caching strategies
self.addEventListener('fetch', event => {
	const url = new URL(event.request.url);
	const isImage = event.request.destination === 'image' || 
					/\.(jpg|jpeg|png|gif|webp|avif|svg)$/i.test(url.pathname);
	const isAPI = url.pathname.startsWith('/api/');

	// Strategy 1: Cache-first for static assets
	if (url.pathname.match(/\.(js|css|woff|woff2|ttf)$/i)) {
		return event.respondWith(
			caches.match(event.request).then(response => {
				return response || fetch(event.request).then(response => {
					return caches.open(STATIC_CACHE).then(cache => {
						cache.put(event.request, response.clone());
						return response;
					});
				});
			}).catch(() => new Response('Offline', { status: 503 }))
		);
	}

	// Strategy 2: Cache-first for images (with size limit)
	if (isImage) {
		return event.respondWith(
			caches.match(event.request).then(response => {
				return response || fetch(event.request).then(response => {
					// Only cache successful responses
					if (response.status === 200) {
						const clone = response.clone();
						caches.open(IMAGE_CACHE).then(cache => {
							cache.put(event.request, clone);
							// Optional: implement LRU cleanup
							pruneImageCache();
						});
					}
					return response;
				}).catch(() => {
					// Return offline placeholder if no cache
					return caches.match(event.request) || 
						   new Response('Image unavailable', { 
							   status: 404,
							   headers: { 'Content-Type': 'text/plain' }
						   });
				});
			})
		);
	}

	// Strategy 3: Network-first for API calls
	if (isAPI) {
		return event.respondWith(
			fetch(event.request).then(response => {
				if (response.status === 200) {
					const clone = response.clone();
					caches.open(API_CACHE).then(cache => {
						cache.put(event.request, clone);
					});
				}
				return response;
			}).catch(() => {
				return caches.match(event.request) || 
					   new Response(
						   JSON.stringify({ error: 'Offline' }), 
						   { 
							   status: 503,
							   headers: { 'Content-Type': 'application/json' }
						   }
					   );
			})
		);
	}

	// Default: network-first
	return event.respondWith(
		fetch(event.request).catch(() => {
			return caches.match(event.request) || 
				   new Response('Offline', { status: 503 });
		})
	);
});

// Helper: Prune old images to prevent cache bloat
async function pruneImageCache() {
	const cache = await caches.open(IMAGE_CACHE);
	const keys = await cache.keys();

	// Maximum 500 images in Service Worker cache
	if (keys.length > 500) {
		// Remove oldest images (this is simple FIFO; could be improved to LRU)
		const toRemove = keys.slice(0, keys.length - 500);
		toRemove.forEach(key => cache.delete(key));
	}
}
```

### HTTP Cache Headers (Website Configuration)

For optimal caching, configure Netlify to serve appropriate cache headers:

**netlify.toml** (or Netlify dashboard headers):
```toml
# Static assets - cache for 1 year (versioned URLs)
[[headers]]
for = "/css/*.css"
[headers.values]
	Cache-Control = "public, max-age=31536000, immutable"

[[headers]]
for = "/js/*.js"
[headers.values]
	Cache-Control = "public, max-age=31536000, immutable"

[[headers]]
for = "/fonts/*"
[headers.values]
	Cache-Control = "public, max-age=31536000, immutable"

# Images - cache for 30 days (from CDNs, can be revalidated)
[[headers]]
for = "*.jpg"
[headers.values]
	Cache-Control = "public, max-age=2592000"

[[headers]]
for = "*.jpeg"
[headers.values]
	Cache-Control = "public, max-age=2592000"

[[headers]]
for = "*.png"
[headers.values]
	Cache-Control = "public, max-age=2592000"

[[headers]]
for = "*.webp"
[headers.values]
	Cache-Control = "public, max-age=2592000"

# HTML - always validate before using
[[headers]]
for = "*.html"
[headers.values]
	Cache-Control = "public, max-age=3600, must-revalidate"

# API calls - no caching (fresh always)
[[headers]]
for = "/api/*"
[headers.values]
	Cache-Control = "no-cache, no-store, must-revalidate"

# Service Worker - check for updates frequently
[[headers]]
for = "/service-worker.js"
[headers.values]
	Cache-Control = "public, max-age=3600"
```

### Image URL Versioning

To enable cache invalidation when images change:

**Current (no version):**
```
https://image.tmdb.org/t/p/w342/poster.jpg
```

**Better (with version in query):**
```
https://image.tmdb.org/t/p/w342/poster.jpg?v=2024.1.15
```

This allows cache busting by changing the version parameter when image data is updated.

---

## Cache Strategies Summary

| Layer | Type | Strategy | TTL | Scope |
|-------|------|----------|-----|-------|
| Browser | Service Worker | Cache-first | 1 year (versioned) | Static assets, CSS, JS, Fonts |
| Browser | Service Worker | Cache-first | 30 days | Images from CDNs |
| Browser | Service Worker | Network-first | Real-time | API calls |
| Native | ImageCacheManager | Request interception | 30 days | Movie posters, thumbnails |
| Native | Statistics | Cleanup trigger | — | Every 10 pages or >300MB |

---

## Performance Benefits

### Expected Improvements

1. **First Page Load**: Service Worker + browser cache + native cache
   - Static assets served from browser cache
   - Images either from Service Worker or native Android cache
   - Estimated saving: 40-60% bandwidth reduction

2. **Subsequent Navigation**: All three layers active
   - Static assets cached by browser/Service Worker
   - Movie images cached by native manager
   - Estimated saving: 70-85% bandwidth reduction

3. **Offline Usage**: Service Worker fallback + Native cache
   - Previously viewed content remains available
   - Images display from native cache even without network
   - HTML pages fail gracefully (API falls back to cache or error)

### Monitoring & Debugging

**Native Cache Status** (LogCat):
```
[ImageCacheManager] Served https://image.tmdb.org/... from cache (45678 bytes)
[CacheInterceptor] Triggering cache cleanup
[CacheStatisticsService] Cache: 245 images, 156.34 MB
```

**Service Worker Status** (Browser DevTools):
- Chrome DevTools → Application → Service Workers → See status
- See cached requests in Application → Cache Storage
- Network tab shows requests served from cache (from ServiceWorker)

---

## Deployment Instructions

### Android App (Native Tier)

1. **No additional deployment needed** – already implemented and compiled
2. Build APK: `dotnet publish -c Release -f net10.0-android`
3. Deploy to device: `adb install bin/Release/net10.0-android/com.DevLink.Un0tv.apk`

### Firebase Website (Browser Tier)

**Implementation Files Created**:
- `public/service-worker.js` – Complete Service Worker with movie preview caching
- `public/sw-manager.js` – Service Worker registration and management
- `firebase.json` – Firebase Hosting configuration with cache headers
- `IMPLEMENTATION_MOVIE_CACHING.md` – Complete step-by-step guide

**Deployment Steps**:

1. **Copy configuration files**:
   - Place `firebase.json` in your project root
   - Place `public/service-worker.js` in your public folder
   - Place `public/sw-manager.js` in your public folder

2. **Update index.html**:
   ```html
   <script src="/sw-manager.js"></script>
   ```

3. **Test locally**:
   ```bash
   firebase serve
   ```
   - Open DevTools → Application → Service Workers (should show "activated")
   - Open Network tab and verify cached responses

4. **Deploy to Firebase**:
   ```bash
   firebase deploy --only hosting
   ```

5. **Verify on production**:
   - Visit https://movince-f5159.web.app
   - Open DevTools → Network tab
   - Browse movie carousel (first load downloads images)
   - Reload page (second load serves from cache – showing "from ServiceWorker")

### Verification Checklist

- [ ] Android app builds successfully with new cache code
- [ ] WebView loads images (with or without cache)
- [ ] Images appear faster on second load
- [ ] Device cache directory exists: `/data/data/com.DevLink.Un0tv/files/image-cache`
- [ ] Service Worker registered on website
- [ ] Movie posters/backdrops are cached (check Network tab)
- [ ] Offline mode shows cached images
- [ ] Cache Storage visible in browser DevTools
- [ ] Network requests show cache hits (green)
- [ ] Cache cleanup triggers after 10 page loads
- [ ] Login and authentication still work
- [ ] Remote control navigation still responsive

---

## Configuration & Tuning

### Android Native Cache Limits

Edit `Un0Tv/Services/CacheStatisticsService.cs`:
```csharp
private const double MaxCacheSizeMB = 300;      // Increase for more caching
private const int MaxImageCount = 500;           // Increase for more images
```

**Recommendations:**
- **Low-end devices** (< 4 GB RAM): 100 MB, 200 images
- **Mid-range devices** (4-8 GB RAM): 300 MB, 500 images (default)
- **High-end devices** (> 8 GB RAM): 500 MB, 1000 images

### Expiration Policy

**Native Images**: 30 days (hardcoded in ImageCacheManager)
```csharp
ExpiresAt = now.AddDays(30)
```

To change: edit `Un0Tv/Services/ImageCacheManager.cs` line ~262

**Service Worker Images**: 30 days (via HTTP Cache-Control header)
**Static Assets**: 1 year (versioned URLs ensure cache breaking)

---

## Troubleshooting

### Images not caching

1. **Check whitelist**: Verify host is in CacheInterceptor.CacheableHosts
2. **Check cache directory permissions**: Ensure app has write access
3. **Verify image URL format**: Must include image extension (.jpg, .png, etc.)
4. **LogCat output**: Look for "[ImageCacheManager]" or "[CacheInterceptor]" messages

### Service Worker not working

1. **Check registration**: Are there errors in console?
2. **Scope mismatch**: Ensure Service Worker is at root or correct scope
3. **HTTPS required**: Service Workers only work over HTTPS (or localhost)
4. **Cache storage disabled**: Check browser privacy settings

### Cache getting too large

1. **Manual cleanup**: App calls CacheStatisticsService.ClearAllAsync()
2. **Automatic cleanup**: Happens when exceeding 300 MB or 500 images
3. **Reduce limits**: Lower MaxCacheSizeMB or MaxImageCount in code

### Performance still slow

1. **Check network**: Poor connectivity makes all caches less effective
2. **Image sizes**: Large original images take longer even from cache
3. **Device storage**: Full device storage causes cache evictions
4. **Update Service Worker**: Ensure latest version is active

---

## Future Enhancements

1. **LRU Eviction**: Replace time-based cleanup with Least Recently Used
2. **Adaptive Sizing**: Adjust cache size based on free device storage
3. **Preloading**: Intelligently preload popular images during idle time
4. **Compression**: Compress images in cache if appropriate
5. **Analytics**: Track cache hit rates and performance improvements
6. **Manual Cache Clear UI**: Add settings screen to clear cache
7. **Bandwidth Meter**: Show bandwidth saved by caching

---

## References

- [Android WebView Caching](https://developer.android.com/reference/android/webkit/WebView)
- [Service Worker API](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API)
- [Cache API](https://developer.mozilla.org/en-US/docs/Web/API/Cache)
- [HTTP Caching](https://developer.mozilla.org/en-US/docs/Web/HTTP/Caching)
- [Netlify Headers Configuration](https://docs.netlify.com/routing/headers/)
