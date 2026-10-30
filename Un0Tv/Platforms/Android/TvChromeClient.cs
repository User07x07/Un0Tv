using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AndroidView = Android.Views.View;
using AndroidWebView = Android.Webkit.WebView;

namespace Un0Tv;

// Shows the pop-up windows the site opens (the Google sign-in window) in a
// full-screen layer above the page, so the D-pad reaches the window itself and
// the page's own focus movement handles the arrows there too.
internal sealed class TvChromeClient : WebChromeClient
{
    private FrameLayout? _host;

    public override bool OnCreateWindow(AndroidWebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (resultMsg?.Obj is not AndroidWebView.WebViewTransport transport)
            return false;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return false;

        var popup = new AndroidWebView(activity);
        TvWebView.Apply(popup);
        popup.SetWebChromeClient(this);
        popup.SetWebViewClient(new PopupClient(this));

        var host = EnsureHost(activity);
        host.AddView(popup, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
        host.Visibility = ViewStates.Visible;

        TvWebView.SetPopup(popup);
        TvWebView.Focus(popup);

        transport.WebView = popup;
        resultMsg.SendToTarget();
        return true;
    }

    public override void OnCloseWindow(AndroidWebView? window)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
            activity.RunOnUiThread(ClosePopup);
        else
            ClosePopup();
    }

    internal void ClosePopup()
    {
        var popup = TvWebView.Popup;
        TvWebView.SetPopup(null);

        if (popup is not null)
        {
            _host?.RemoveView(popup);
            popup.StopLoading();
            popup.LoadUrl("about:blank");
            popup.Destroy();
        }

        if (_host is not null)
            _host.Visibility = ViewStates.Gone;

        // Give the remote back to the page underneath.
        TvWebView.Focus(TvWebView.Main);
    }

    private FrameLayout EnsureHost(Activity activity)
    {
        if (_host is not null && _host.IsAttachedToWindow)
            return _host;

        _host = new FrameLayout(activity)
        {
            Focusable = true,
            FocusableInTouchMode = true
        };
        _host.SetBackgroundColor(Android.Graphics.Color.Black);
        _host.Visibility = ViewStates.Gone;

        if (activity.Window?.DecorView is ViewGroup decor)
        {
            decor.AddView(_host, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        }

        return _host;
    }

    // Applies the ad filter to every page the sign-in window loads. The window
    // keeps its own focus movement, so the arrows work there without the app
    // catching its keys (which would break the on-screen keyboard).
    private sealed class PopupClient : WebViewClient
    {
        // The window host that owns this window, so an advert window can be
        // closed from inside the page-loaded callback.
        private readonly TvChromeClient _owner;

        public PopupClient(TvChromeClient owner) => _owner = owner;

        public override bool ShouldOverrideUrlLoading(
            AndroidWebView? view, IWebResourceRequest? request)
        {
            // A window that is being steered at an ad host is taken down before
            // it can load anything.
            if (request is not null && RequestFilter.IsBlocked(request.Url?.ToString()))
            {
                CloseOwner();
                return true;
            }
            return base.ShouldOverrideUrlLoading(view, request);
        }

        private void CloseOwner()
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is not null)
                activity.RunOnUiThread(_owner.ClosePopup);
            else
                _owner.ClosePopup();
        }

        public override Android.Webkit.WebResourceResponse? ShouldInterceptRequest(
            AndroidWebView? view, IWebResourceRequest? request)
        {
            if (request is not null && RequestFilter.IsBlocked(request.Url?.ToString()))
                return RequestFilter.Empty();

            return base.ShouldInterceptRequest(view, request);
        }

        public override void OnPageFinished(AndroidWebView? view, string? url)
        {
            base.OnPageFinished(view, url);

            if (view is null)
                return;

            // An advert network can still reach a window through a plain link
            // (target="_blank") rather than window.open. If what arrived is an
            // ad host, take the window straight back down - the viewer should
            // never see it.
            if (RequestFilter.IsBlocked(url))
            {
                CloseOwner();
                return;
            }
        }
    }

    // ---- Fullscreen video -------------------------------------------------
    // When a player asks for fullscreen (its own button, or the site's), the
    // video view is shown over the whole screen. Back leaves fullscreen
    // instead of dropping out of the app.
    private AndroidView? _fullView;
    private FrameLayout? _fullGroup;

    public bool IsFullscreen => _fullView is not null;

    public override void OnShowCustomView(AndroidView? view, ICustomViewCallback? callback)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity?.Window?.DecorView is not ViewGroup decor)
            return;

        // Android 7+ sends a placeholder custom view with no size when the
        // page merely changes orientation; ignoring it keeps the screen from
        // going black.
        if (view is FrameLayout fl && fl.LayoutParameters is { } lp
            && lp.Width == 0 && lp.Height == 0)
            return;

        if (_fullView is not null)
        {
            callback?.OnCustomViewHidden();
            return;
        }

        _fullView = view;
        var group = new FrameLayout(activity)
        {
            Focusable = true,
            FocusableInTouchMode = true
        };
        group.SetBackgroundColor(Android.Graphics.Color.Black);
        _fullGroup = group;

        decor.AddView(group, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        if (view is not null)
        {
            group.AddView(view, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        }

        TvWebView.Focus(TvWebView.Popup ?? TvWebView.Main);
    }

    public override void OnHideCustomView()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
            activity.RunOnUiThread(EndFullscreen);
        else
            EndFullscreen();
    }

    internal void EndFullscreen()
    {
        if (_fullView is not null)
        {
            _fullGroup?.RemoveView(_fullView);
            _fullView = null;
        }

        if (_fullGroup is not null)
        {
            if (_fullGroup.Parent is ViewGroup parent)
                parent.RemoveView(_fullGroup);
            _fullGroup = null;
        }

        TvWebView.Focus(TvWebView.Popup ?? TvWebView.Main);
    }
}
