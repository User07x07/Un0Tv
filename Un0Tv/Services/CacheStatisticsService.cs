using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Un0Tv.Services;

/// <summary>
/// Provides cache statistics and management capabilities.
/// Monitors cache size, image count, and expiration.
/// </summary>
internal class CacheStatisticsService
{
    private readonly ImageCacheManager _cacheManager;
    private CacheStatistics? _lastStatistics;
    private DateTime _lastStatisticsUpdate = DateTime.MinValue;
    private readonly TimeSpan StatisticsCacheDuration = TimeSpan.FromMinutes(5);

    // Target cache size: 500 images or ~300 MB
    private const double MaxCacheSizeMB = 300;
    private const int MaxImageCount = 500;

    public CacheStatisticsService(ImageCacheManager cacheManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    /// <summary>
    /// Gets current cache statistics, using cached values if recent.
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Return cached statistics if still valid and not forced
        if (!forceRefresh && _lastStatistics is not null && 
            (now - _lastStatisticsUpdate) < StatisticsCacheDuration)
        {
            return _lastStatistics;
        }

        // Fetch fresh statistics
        _lastStatistics = await _cacheManager.GetStatisticsAsync(cancellationToken);
        _lastStatisticsUpdate = now;

        return _lastStatistics;
    }

    /// <summary>
    /// Checks if cache cleanup is needed based on size and image count thresholds.
    /// </summary>
    public async Task<bool> NeedsCleanupAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(forceRefresh: true, cancellationToken);
        return stats.TotalSizeMB > MaxCacheSizeMB || stats.TotalImageCount > MaxImageCount;
    }

    /// <summary>
    /// Performs cache cleanup by removing expired images and checking size limits.
    /// Returns the number of images removed.
    /// </summary>
    public async Task<int> PerformCleanupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[CacheStatisticsService] Starting cache cleanup");

            // First, remove all expired images
            var expiredCount = await _cacheManager.RemoveExpiredImagesAsync(cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[CacheStatisticsService] Removed {expiredCount} expired images");

            // Check if we still need to reduce size
            var stats = await GetStatisticsAsync(forceRefresh: true, cancellationToken);

            if (stats.TotalSizeMB > MaxCacheSizeMB || stats.TotalImageCount > MaxImageCount)
            {
                // Need to implement LRU eviction or additional cleanup
                System.Diagnostics.Debug.WriteLine(
                    $"[CacheStatisticsService] Cache still above limits: {stats.TotalImageCount} images, {stats.TotalSizeMB:F2} MB");
            }

            return expiredCount;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheStatisticsService] Error during cleanup: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Clears all cached images and resets statistics.
    /// </summary>
    public async Task<bool> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[CacheStatisticsService] Clearing all cached images");

            var result = await _cacheManager.ClearCacheAsync(cancellationToken);

            if (result)
            {
                _lastStatistics = new CacheStatistics();
                _lastStatisticsUpdate = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine("[CacheStatisticsService] Cache cleared successfully");
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheStatisticsService] Error clearing cache: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets a formatted cache statistics summary for display.
    /// </summary>
    public async Task<string> GetStatisticsSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await GetStatisticsAsync(cancellationToken: cancellationToken);
            return $"Cache: {stats.TotalImageCount} images, {stats.TotalSizeMB:F2} MB";
        }
        catch
        {
            return "Cache: Unavailable";
        }
    }

    /// <summary>
    /// Preloads commonly accessed images into cache.
    /// </summary>
    public async Task PreloadCommonImagesAsync(CancellationToken cancellationToken = default)
    {
        // This is a placeholder for future enhancement
        // Will preload popular images from the dashboard
        await Task.CompletedTask;
    }
}
