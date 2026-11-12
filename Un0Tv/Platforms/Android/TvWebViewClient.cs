#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Webkit;
using Microsoft.Maui.Handlers;
using AndroidWebView = Android.Webkit.WebView;

namespace Un0Tv;

// The browser client for the main page. It extends the framework's own
// client so the page's Navigated / Navigating events keep firing, drops
// ad requests on the way through, and now serves cached images when available.
internal sealed class TvWebViewClient : Microsoft.Maui.Platform.MauiWebViewClient
{
    private static int _pageLoadCount;

    public TvWebViewClient(WebViewHandler handler) : base(handler) { }

    // A new page means the movie is gone: the app's panels close with it, so a
    // command bar is never left over the site.
    public override void OnPageFinished(AndroidWebView? view, string? url)
    {
        base.OnPageFinished(view, url);

        MediaPanel.Hide();
        TvWebView.SetMoviePlaying(false);

        // Trigger cache cleanup periodically (every 10 pages)
        if (++_pageLoadCount % 10 == 0)
            CacheInterceptor.TriggerCleanupIfNeeded();
    }

    public override WebResourceResponse? ShouldInterceptRequest(
        AndroidWebView? view, IWebResourceRequest? request)
    {
        if (request is null)
            return null;

        var url = request.Url?.ToString();

        // First: Apply ad/tracker filtering (existing behavior)
        if (RequestFilter.IsBlocked(url))
            return RequestFilter.Empty();

        // Second: Try to serve from cache for images
        // Non-blocking: use timeout to prevent UI freeze
        var cachedImage = GetCachedImageSync(url);
        if (cachedImage is not null)
        {
            return CreateImageResponse(cachedImage, GetMimeType(url));
        }

        return base.ShouldInterceptRequest(view, request);
    }

    /// <summary>
    /// Synchronous wrapper to get cached image within timeout.
    /// </summary>
    private static byte[]? GetCachedImageSync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var task = Task.Run(async () => await CacheInterceptor.GetImageAsync(url, cts.Token));

            if (task.Wait(200))
                return task.Result;
        }
        catch
        {
            // Timeouts and errors are non-fatal; just skip caching
        }

        return null;
    }

    /// <summary>
    /// Creates a WebResourceResponse from cached image data.
    /// </summary>
    private static WebResourceResponse? CreateImageResponse(byte[] imageData, string mimeType)
    {
        try
        {
            var stream = new System.IO.MemoryStream(imageData);
            var response = new WebResourceResponse(mimeType, "utf-8", 200, "OK", null, stream);
            return response;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TvWebViewClient] Error creating image response: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Determines MIME type from URL.
    /// </summary>
    private static string GetMimeType(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "image/jpeg";

        var path = url.Split('?')[0].ToLower();

        return path.EndsWith(".png") ? "image/png"
            : path.EndsWith(".gif") ? "image/gif"
            : path.EndsWith(".webp") ? "image/webp"
            : path.EndsWith(".avif") ? "image/avif"
            : "image/jpeg"; // Default fallback
    }
}
#endif
