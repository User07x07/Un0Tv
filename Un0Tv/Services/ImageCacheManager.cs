using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Un0Tv.Services;

/// <summary>
/// Manages persistent image caching in app-private storage.
/// Provides asynchronous image retrieval with fallback to network download.
/// </summary>
internal class ImageCacheManager
{
    private static readonly string CacheDirectory =
        Path.Combine(FileSystem.AppDataDirectory, "image-cache");

    private static readonly string MetadataDirectory =
        Path.Combine(FileSystem.AppDataDirectory, "cache-metadata");

    // Track in-flight downloads to prevent duplicate downloads for the same URL
    private static readonly Dictionary<string, Task<CachedImage?>> InFlightDownloads = new();
    private static readonly object InFlightLock = new();

    // HTTP client for downloading images
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // Supported image extensions
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif"
    };

    public ImageCacheManager()
    {
        InitializeCacheDirectories();
    }

    /// <summary>
    /// Initializes cache directories if they don't exist.
    /// </summary>
    private static void InitializeCacheDirectories()
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(MetadataDirectory);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to create cache directories: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to retrieve a cached image, falling back to download if not found.
    /// </summary>
    public async Task<CachedImage?> GetCachedImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        try
        {
            // Check for in-flight download first to prevent duplicates
            lock (InFlightLock)
            {
                if (InFlightDownloads.TryGetValue(imageUrl, out var inFlight))
                {
                    return inFlight.Result;
                }
            }

            // Try to load from cache first
            var cached = await LoadCachedImageAsync(imageUrl, cancellationToken);
            if (cached is not null)
                return cached;

            // Not in cache, download it
            return await DownloadImageAsync(imageUrl, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Download cancelled for {imageUrl}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to get image from cache/download: {imageUrl}, Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads an image and saves it to cache.
    /// Prevents duplicate downloads via in-flight tracking.
    /// </summary>
    public async Task<CachedImage?> DownloadImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        // Check for in-flight download
        Task<CachedImage?>? downloadTask;
        lock (InFlightLock)
        {
            if (InFlightDownloads.TryGetValue(imageUrl, out downloadTask))
            {
                try
                {
                    return downloadTask.Result;
                }
                catch
                {
                    return null;
                }
            }

            // Create and track the download task
            downloadTask = PerformDownloadAsync(imageUrl, cancellationToken);
            InFlightDownloads[imageUrl] = downloadTask;
        }

        try
        {
            return await downloadTask;
        }
        finally
        {
            // Remove from in-flight tracking
            lock (InFlightLock)
            {
                InFlightDownloads.Remove(imageUrl);
            }
        }
    }

    /// <summary>
    /// Performs the actual download without in-flight tracking.
    /// </summary>
    private async Task<CachedImage?> PerformDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate URL
            try
            {
                _ = new Uri(imageUrl);
            }
            catch
            {
                return null;
            }

            // Download the image
            var response = await Http.GetAsync(imageUrl, HttpCompletionOption.ResponseContentRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Download failed for {imageUrl}: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (content.Length == 0)
                return null;

            // Validate that it's actually an image
            if (!IsValidImageContent(content))
                return null;

            // Save to cache
            return await SaveImageAsync(imageUrl, content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] HTTP error downloading {imageUrl}: {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Download cancelled for {imageUrl}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Error downloading image {imageUrl}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads a cached image from disk if it exists and hasn't expired.
    /// </summary>
    private async Task<CachedImage?> LoadCachedImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GenerateCacheKey(imageUrl);
            var cachePath = GetCachePath(cacheKey);
            var metadataPath = GetMetadataPath(cacheKey);

            // Check if both cache file and metadata exist
            if (!File.Exists(cachePath) || !File.Exists(metadataPath))
                return null;

            // Load metadata to check expiration
            var metadata = await LoadMetadataAsync(metadataPath, cancellationToken);
            if (metadata is null || IsExpired(metadata))
            {
                // Remove expired cache entry
                await RemoveExpiredImageAsync(cacheKey, cancellationToken);
                return null;
            }

            // Load image data
            var imageData = await File.ReadAllBytesAsync(cachePath, cancellationToken);

            if (imageData.Length == 0)
                return null;

            // Update last access time
            metadata.LastAccessTime = DateTime.UtcNow;
            await SaveMetadataAsync(metadataPath, metadata, cancellationToken);

            return new CachedImage
            {
                Url = imageUrl,
                Data = imageData,
                MimeType = metadata.MimeType ?? GetMimeType(cachePath),
                CachePath = cachePath,
                CachedAt = metadata.CachedAt,
                ExpiresAt = metadata.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to load cached image for {imageUrl}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves an image to cache with metadata.
    /// </summary>
    public async Task<CachedImage?> SaveImageAsync(
        string imageUrl,
        byte[] imageData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || imageData is null || imageData.Length == 0)
            return null;

        try
        {
            var cacheKey = GenerateCacheKey(imageUrl);
            var cachePath = GetCachePath(cacheKey);
            var metadataPath = GetMetadataPath(cacheKey);

            // Ensure directories exist
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);

            // Save image data
            await File.WriteAllBytesAsync(cachePath, imageData, cancellationToken);

            // Determine MIME type
            var mimeType = GetMimeTypeFromContent(imageData) ?? GetMimeType(cachePath);

            // Create and save metadata
            var now = DateTime.UtcNow;
            var metadata = new CacheMetadata
            {
                Url = imageUrl,
                CacheKey = cacheKey,
                MimeType = mimeType,
                FileSize = imageData.Length,
                CachedAt = now,
                LastAccessTime = now,
                ExpiresAt = now.AddDays(30) // 30-day expiration
            };

            await SaveMetadataAsync(metadataPath, metadata, cancellationToken);

            return new CachedImage
            {
                Url = imageUrl,
                Data = imageData,
                MimeType = mimeType,
                CachePath = cachePath,
                CachedAt = metadata.CachedAt,
                ExpiresAt = metadata.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to save image to cache: {imageUrl}, Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Removes a specific cached image.
    /// </summary>
    public async Task<bool> RemoveImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GenerateCacheKey(imageUrl);
            return await RemoveExpiredImageAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to remove image: {imageUrl}, Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes all expired images from cache.
    /// </summary>
    public async Task<int> RemoveExpiredImagesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(MetadataDirectory))
                return 0;

            var removed = 0;
            var metadataFiles = Directory.GetFiles(MetadataDirectory, "*.json");

            foreach (var metadataPath in metadataFiles)
            {
                try
                {
                    var metadata = await LoadMetadataAsync(metadataPath, cancellationToken);
                    if (metadata is not null && IsExpired(metadata))
                    {
                        var cacheKey = Path.GetFileNameWithoutExtension(metadataPath);
                        if (await RemoveExpiredImageAsync(cacheKey, cancellationToken))
                            removed++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Error processing metadata file {metadataPath}: {ex.Message}");
                }
            }

            return removed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Error removing expired images: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Clears all cached images.
    /// </summary>
    public async Task<bool> ClearCacheAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (Directory.Exists(CacheDirectory))
                Directory.Delete(CacheDirectory, true);

            if (Directory.Exists(MetadataDirectory))
                Directory.Delete(MetadataDirectory, true);

            // Re-initialize directories
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(MetadataDirectory);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to clear cache: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets cache statistics including total size and image count.
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = new CacheStatistics();

            if (!Directory.Exists(MetadataDirectory))
                return stats;

            var metadataFiles = Directory.GetFiles(MetadataDirectory, "*.json");
            long totalSize = 0;
            int imageCount = 0;

            foreach (var metadataPath in metadataFiles)
            {
                try
                {
                    var metadata = await LoadMetadataAsync(metadataPath, cancellationToken);
                    if (metadata is not null && !IsExpired(metadata))
                    {
                        var cacheKey = Path.GetFileNameWithoutExtension(metadataPath);
                        var cachePath = GetCachePath(cacheKey);

                        if (File.Exists(cachePath))
                        {
                            totalSize += metadata.FileSize;
                            imageCount++;
                        }
                    }
                }
                catch
                {
                    // Skip files with errors
                }
            }

            stats.TotalImageCount = imageCount;
            stats.TotalSizeBytes = totalSize;
            stats.TotalSizeMB = totalSize / (1024.0 * 1024.0);

            return stats;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to get cache statistics: {ex.Message}");
            return new CacheStatistics();
        }
    }

    /// <summary>
    /// Preloads multiple images into cache.
    /// </summary>
    public async Task PreloadImagesAsync(
        IEnumerable<string> imageUrls,
        CancellationToken cancellationToken = default)
    {
        var downloadTasks = new List<Task>();

        foreach (var url in imageUrls)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                // Don't await each individual download to allow parallel loading
                downloadTasks.Add(GetCachedImageAsync(url, cancellationToken));
            }
        }

        try
        {
            await Task.WhenAll(downloadTasks);
        }
        catch
        {
            // Preload failures are non-critical
        }
    }

    // ---- Helper Methods ----

    private static string GenerateCacheKey(string imageUrl)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(imageUrl));
        return Convert.ToHexString(hash).ToLower();
    }

    private static string GetCachePath(string cacheKey)
    {
        var subdir = cacheKey[..2];
        return Path.Combine(CacheDirectory, subdir, cacheKey);
    }

    private static string GetMetadataPath(string cacheKey)
    {
        var subdir = cacheKey[..2];
        return Path.Combine(MetadataDirectory, subdir, $"{cacheKey}.json");
    }

    private static async Task<bool> RemoveExpiredImageAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachePath = GetCachePath(cacheKey);
            var metadataPath = GetMetadataPath(cacheKey);

            if (File.Exists(cachePath))
                File.Delete(cachePath);

            if (File.Exists(metadataPath))
                File.Delete(metadataPath);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to remove expired image {cacheKey}: {ex.Message}");
            return false;
        }
    }

    private static async Task<CacheMetadata?> LoadMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            return System.Text.Json.JsonSerializer.Deserialize<CacheMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveMetadataAsync(
        string metadataPath,
        CacheMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(metadata);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheManager] Failed to save metadata: {ex.Message}");
        }
    }

    private static bool IsExpired(CacheMetadata metadata)
    {
        return DateTime.UtcNow > metadata.ExpiresAt;
    }

    private static bool IsValidImageContent(byte[] data)
    {
        if (data.Length < 4)
            return false;

        // Check magic bytes for common image formats
        return data[0] == 0xFF && data[1] == 0xD8 // JPEG
            || (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) // PNG
            || (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) // GIF
            || (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) // RIFF (WebP)
            || data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C; // AVIF signature
    }

    private static string? GetMimeTypeFromContent(byte[] data)
    {
        if (data.Length < 4)
            return null;

        return data[0] == 0xFF && data[1] == 0xD8 ? "image/jpeg"
            : (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) ? "image/png"
            : (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) ? "image/gif"
            : (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) ? "image/webp"
            : data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C ? "image/avif"
            : null;
    }

    private static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            _ => "image/jpeg" // Default fallback
        };
    }
}

/// <summary>
/// Represents a cached image with its metadata.
/// </summary>
public class CachedImage
{
    public string? Url { get; set; }
    public byte[]? Data { get; set; }
    public string? MimeType { get; set; }
    public string? CachePath { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Metadata for a cached image file.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
internal class CacheMetadata
{
    public string? Url { get; set; }
    public string? CacheKey { get; set; }
    public string? MimeType { get; set; }
    public long FileSize { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime LastAccessTime { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Cache statistics for monitoring.
/// </summary>
public class CacheStatistics
{
    public int TotalImageCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public double TotalSizeMB { get; set; }
}
