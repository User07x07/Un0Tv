#if ANDROID
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Un0Tv.Services;

namespace Un0Tv;

/// <summary>
/// Intercepts WebView requests to serve cached images instead of downloading them.
/// Works in conjunction with TvWebViewClient.ShouldInterceptRequest.
/// </summary>
internal class CacheInterceptor
{
    private static readonly ImageCacheManager CacheManager = new();
    private static readonly CacheStatisticsService StatisticsService = 
        new(CacheManager);

    // Whitelisted image hosts that should be cached
    private static readonly string[] CacheableHosts = new[]
    {
        "themoviedb.org",
        "image.tmdb.org",
        "images.tmdb.org",
        "cdn.jsdelivr.net",
        "cdn.discordapp.com",
        "cloudflare.com",
        "akamaihd.net",
        "fastly.net",
        "stackpath.bootstrapcdn.com",
        "cdnjs.cloudflare.com",
        "unpkg.com",
        "googleapis.com",
        "gstatic.com"
    };

    /// <summary>
    /// Gets a cached image or downloads it if not in cache.
    /// Returns null if the image cannot be cached or downloaded.
    /// </summary>
    public static async Task<byte[]?> GetImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Skip caching for non-image URLs
            if (!IsImageUrl(imageUrl))
                return null;

            // Skip caching if host is not whitelisted
            if (!IsWhitelistedHost(imageUrl))
                return null;

            // Get from cache or download
            var cached = await CacheManager.GetCachedImageAsync(imageUrl, cancellationToken);

            if (cached?.Data is not null)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheInterceptor] Served {imageUrl} from cache ({cached.Data.Length} bytes)");
                return cached.Data;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheInterceptor] Error getting image {imageUrl}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if cache needs cleanup and performs it if necessary.
    /// Non-blocking; runs in background.
    /// </summary>
    public static void TriggerCleanupIfNeeded()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (await StatisticsService.NeedsCleanupAsync())
                {
                    System.Diagnostics.Debug.WriteLine("[CacheInterceptor] Triggering cache cleanup");
                    await StatisticsService.PerformCleanupAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheInterceptor] Error during cleanup trigger: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Clears all cached images. Call this when user manually clears cache.
    /// </summary>
    public static async Task<bool> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await StatisticsService.ClearAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheInterceptor] Error clearing cache: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets cache statistics for display.
    /// </summary>
    public static async Task<CacheStatistics?> GetCacheStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StatisticsService.GetStatisticsAsync(forceRefresh: true, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheInterceptor] Error getting statistics: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets a formatted cache summary string.
    /// </summary>
    public static async Task<string> GetCacheSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StatisticsService.GetStatisticsSummaryAsync(cancellationToken);
        }
        catch
        {
            return "Cache: Unavailable";
        }
    }

    // ---- Helper Methods ----

    private static bool IsImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Remove query parameters for extension check
        var path = url.Split('?')[0].ToLower();

        // Check for common image extensions
        return path.EndsWith(".jpg")
            || path.EndsWith(".jpeg")
            || path.EndsWith(".png")
            || path.EndsWith(".gif")
            || path.EndsWith(".webp")
            || path.EndsWith(".avif")
            || path.EndsWith(".svg")
            || path.Contains("image")
            || path.Contains("poster")
            || path.Contains("backdrop")
            || path.Contains("thumb");
    }

    private static bool IsWhitelistedHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();

            // Direct exact match
            if (CacheableHosts.Contains(host))
                return true;

            // Domain suffix match
            return CacheableHosts.Any(whitelistedHost =>
                host.EndsWith("." + whitelistedHost, StringComparison.OrdinalIgnoreCase)
                || host.Equals(whitelistedHost, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
#endif
