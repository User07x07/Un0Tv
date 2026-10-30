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

    public override WebResourceResponse? ShouldInterceptRequest(
        AndroidWebView? view, IWebResourceRequest? request)
    {
        if (request is not null && RequestFilter.IsBlocked(request.Url?.ToString()))
            return RequestFilter.Empty();

        return base.ShouldInterceptRequest(view, request);
    }
}
#endif
