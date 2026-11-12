# UN0 Movie Caching Implementation Guide

## Overview

This guide provides step-by-step instructions to implement the complete movie preview/poster/backdrop caching system for UN0 across both the Firebase website and the MAUI Android TV app.

## Two-Tier Caching Architecture

### Tier 1: Browser/Service Worker (Website - Firebase Hosting)
- **Location**: https://movince-f5159.web.app
- **Technology**: Service Worker + Cache API
- **Purpose**: Cache assets and movie images in browser storage (up to ~50MB depending on browser)
- **Strategy**: Cache-first for images and static assets, Network-first for APIs

### Tier 2: Native Android Cache (MAUI App)
- **Location**: `/data/data/com.DevLink.Un0tv/files/image-cache`
- **Technology**: C# ImageCacheManager + WebView interception
- **Purpose**: Persistent cache of movie images and assets (up to 300MB)
- **Strategy**: Automatic interception of image requests to serve from cache

---

## Website Implementation (Firebase Hosting)

### Step 1: Deploy Service Worker File

**File**: `public/service-worker.js`

This file has been created and should be deployed to your Firebase hosting. It implements:
- Cache-first strategy for movie posters, backdrops, and previews
- Automatic cache updates
- Support for TMDB image URLs
- Offline fallback

**Location**: `public/service-worker.js`

**Deploy Command**:
```bash
firebase deploy --only hosting
```

### Step 2: Configure Firebase Hosting Headers

**File**: `firebase.json`

This configuration file has been created and includes:
- 30-day cache expiration for movie images (posters, backdrops, previews)
- Immutable cache headers for versioned assets
- Security headers (X-Content-Type-Options, X-Frame-Options, X-XSS-Protection)
- Automatic rewrite rules for SPA routing

**Key Cache Rules**:
```json
{
  "source": "**/@(*poster*|*backdrop*|*preview*|*thumb*)",
  "headers": [
	{ "key": "Cache-Control", "value": "public, max-age=2592000" }
  ]
}
```

### Step 3: Register Service Worker in HTML

**File**: `public/sw-manager.js`

Add this script to your main `index.html` file:

```html
<!DOCTYPE html>
<html>
<head>
  <!-- Your other head content -->
</head>
<body>
  <!-- Your page content -->

  <!-- Service Worker Manager -->
  <script src="/sw-manager.js"></script>
</body>
</html>
```

**Features Provided**:
- Automatic Service Worker registration
- Update notifications
- Cache statistics
- Manual cache clearing UI integration
- Periodic cache cleanup (>30 days)

### Step 4: Update index.html

Add this HTML element to display cache status (optional):

```html
<div id="cache-status" style="position: fixed; bottom: 10px; right: 10px; 
	 background: rgba(0,0,0,0.8); color: white; padding: 10px 15px; 
	 border-radius: 5px; font-size: 12px; z-index: 9999;">
  <!-- Cache status will be displayed here -->
</div>
```

**Available Public APIs** (from `sw-manager.js`):
```javascript
// Get cache statistics
window.serviceWorkerManager.getStats()
  .then(stats => console.log('Cache stats:', stats));

// Clear all caches manually
window.serviceWorkerManager.clearCache()
  .then(() => console.log('Caches cleared'));

// Check for updates
window.serviceWorkerManager.checkUpdate();
```

---

## Android TV App Implementation (MAUI)

### Current Status: ✅ ALREADY IMPLEMENTED

The MAUI application already includes complete caching support:

#### 1. **ImageCacheManager** (`Un0Tv/Services/ImageCacheManager.cs`)
- Downloads and caches images with SHA256-based filenames
- Validates image content (magic bytes)
- Supports 30-day expiration
- Manages cache metadata as JSON
- **Supports**: .jpg, .jpeg, .png, .gif, .webp, .avif

#### 2. **CacheInterceptor** (`Un0Tv/Platforms/Android/CacheInterceptor.cs`)
- Validates whitelisted hosts
- Provides cache statistics
- Triggers cache cleanup when needed

#### 3. **TvWebViewClient** Integration
- Automatically intercepts image requests
- Serves from cache when available
- Falls back to network if needed

### Configuration

**Whitelisted Hosts** (already configured):
```csharp
private static readonly string[] CacheableHosts = new[]
{
	"themoviedb.org",
	"image.tmdb.org",
	"images.tmdb.org",
	"cdn.jsdelivr.net",
	"cdnjs.cloudflare.com",
	"cloudflare.com",
	"akamaihd.net",
	"fastly.net",
	"stackpath.bootstrapcdn.com",
	"unpkg.com",
	"googleapis.com",
	"gstatic.com"
};
```

**Image Detection** (already includes movie media types):
```csharp
// Automatically detects:
// - file extensions: .jpg, .jpeg, .png, .gif, .webp, .avif, .svg
// - URL patterns: "image", "poster", "backdrop", "thumb"
```

**Cache Configuration**:
```csharp
private const double MaxCacheSizeMB = 300;      // ~300 MB maximum
private const int MaxImageCount = 500;           // 500 images maximum
```

---

## Testing the Implementation

### Browser-Side Testing

1. **Open DevTools Console**:
   ```javascript
   // Check Service Worker installation
   navigator.serviceWorker.ready.then(reg => {
	 console.log('Service Worker ready:', reg);
   });

   // Get cache statistics
   window.serviceWorkerManager.getStats().then(stats => {
	 console.log('Cached items:', stats.totalItems);
	 console.log('Cached images:', stats.caches['images-v1.0.0']);
   });
   ```

2. **Monitor Network Tab**:
   - First page load: images downloaded from network
   - Second page load: images loaded from cache (check "Size" column shows "from cache")
   - File size should show in parentheses, e.g., "45.3 KB (from disk cache)"

3. **Browse to Movie Carousel**:
   - First load: observe network requests for poster/backdrop images
   - Subsequent loads: notice much faster loading from cache

### Android App Testing

1. **Monitor Logcat**:
   ```
   [CacheInterceptor] Served {imageUrl} from cache ({size} bytes)
   [ImageCacheManager] Cached image: {filename}
   ```

2. **Check Cache Directory**:
   Using ADB:
   ```bash
   adb shell run-as com.DevLink.Un0tv ls -la /data/data/com.DevLink.Un0tv/files/image-cache/
   ```

3. **Test Offline**:
   - Browse to home page (pre-loads movie previews)
   - Disable network/airplane mode
   - Navigate to different pages
   - Movie images should still appear from cache

---

## Performance Improvements Expected

### Load Times
| Scenario | Before Caching | After Caching | Improvement |
|----------|---|---|---|
| First visit | 3-5 seconds | 3-5 seconds | Baseline |
| Carousel reload | 3-5 seconds | 1-2 seconds | 3-5x faster |
| Offline mode | Not available | ~0.5 seconds | Enabled |

### Network Bandwidth
| Scenario | Before | After | Reduction |
|----------|---|---|---|
| Daily browsing | 50-100 MB | 5-10 MB | 80-90% |
| Movie carousel only | 10-20 MB | 1-2 MB | 85-90% |

### User Experience
- Faster page transitions
- Smoother scrolling on Android TV
- Offline content access
- Reduced data plan impact
- Better performance on slow connections

---

## Deployment Checklist

### Firebase Website
- [ ] Copy `firebase.json` to project root
- [ ] Copy `public/service-worker.js` to public folder
- [ ] Copy `public/sw-manager.js` to public folder
- [ ] Add `<script src="/sw-manager.js"></script>` to `index.html`
- [ ] Test locally: `firebase serve`
- [ ] Deploy: `firebase deploy --only hosting`
- [ ] Verify Service Worker registration in browser DevTools

### Android MAUI App (Un0Tv)
- [ ] ✅ ImageCacheManager already configured
- [ ] ✅ CacheInterceptor already integrated
- [ ] ✅ WebView request interception active
- [ ] Rebuild project: `dotnet build`
- [ ] Test on Android TV device
- [ ] Monitor Logcat for cache hits

---

## Troubleshooting

### Service Worker Not Registering

**Browser Console Errors**:
```
Failed to register: bad HTTP response code (404)
```

**Solution**:
- Verify `service-worker.js` is in public folder
- Check firebase.json includes it in public path
- Redeploy: `firebase deploy --only hosting`

### Images Not Being Cached

**Check Whitelisted Hosts**:
```javascript
// In browser console
caches.open('images-v1.0.0').then(cache => {
  cache.keys().then(requests => {
	console.log('Cached URLs:', requests.map(r => r.url));
  });
});
```

**Solution**:
- Verify image URLs contain "poster", "backdrop", or "preview"
- Ensure domain is in whitelisted hosts list
- Check URL uses HTTPS

### Cache Taking Too Much Storage

**Clear Cache**:
```javascript
// In browser console
window.serviceWorkerManager.clearCache();

// On Android app
// Use app settings → Clear Cache
```

**Adjust Size Limits**:
- Browser: Automatic (limited by storage quota)
- Android: Edit `MaxCacheSizeMB` in ImageCacheManager.cs (default 300MB)

---

## Future Enhancements

1. **IndexedDB for Larger Data**: Expand beyond 50MB browser cache
2. **Background Sync**: Queue requests when offline, sync when online
3. **Push Notifications**: Notify users when new content available
4. **Analytics**: Track cache hit rates and performance metrics
5. **Lazy Loading**: Defer image caching until user scrolls to them

---

## References

- [Service Worker API - MDN](https://developer.mozilla.org/docs/Web/API/Service_Worker_API)
- [Cache API - MDN](https://developer.mozilla.org/docs/Web/API/Cache)
- [Firebase Hosting - Cache Behavior](https://firebase.google.com/docs/hosting/serve-dynamic-content)
- [MAUI WebView - Microsoft Docs](https://learn.microsoft.com/dotnet/maui/user-interface/controls/webview)
