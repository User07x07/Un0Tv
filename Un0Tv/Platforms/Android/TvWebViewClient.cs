#if ANDROID
using Android.Webkit;
using Microsoft.Maui.Handlers;
using AndroidWebView = Android.Webkit.WebView;
namespace Un0Tv;

// The browser client for the main page. It extends the framework's own
// client so the page's Navigated / Navigating events keep firing, and drops
// ad requests on the way through.
internal sealed class TvWebViewClient : Microsoft.Maui.Platform.MauiWebViewClient
{
    public TvWebViewClient(WebViewHandler handler) : base(handler) { }

    // A new page means the movie is gone: the app's panels close with it, so a
    // command bar is never left over the site.
    public override void OnPageFinished(AndroidWebView? view, string? url)
    {
        base.OnPageFinished(view, url);

        MediaPanel.Hide();
        TvWebView.SetMoviePlaying(false);
    }

    public override WebResourceResponse? ShouldInterceptRequest(
        AndroidWebView? view, IWebResourceRequest? request)
    {
        if (request is not null && RequestFilter.IsBlocked(request.Url?.ToString()))
            return RequestFilter.Empty();

        return base.ShouldInterceptRequest(view, request);
    }
}
#endif
