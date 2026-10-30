using Android.Webkit;
using Microsoft.Maui.Handlers;
using AndroidWebView = Android.Webkit.WebView;

namespace Un0Tv;

// The site signs in with Google through a pop-up window (Firebase
// signInWithPopup). Android's WebView blocks those pop-ups unless the app
// asks for them, which shows up as "Your browser blocked the pop-up".
//
// This class turns pop-ups on, keeps the sign-in cookies, and remembers which
// browser view the remote should drive right now (the page, or the sign-in
// window sitting over it).
internal static class TvWebView
{
    private static TvChromeClient? _client;
    private static TvWebViewClient? _viewClient;

    public static AndroidWebView? Main { get; private set; }
    public static AndroidWebView? Popup { get; private set; }

    public static bool IsPopupOpen => Popup is not null;

    // Set by the activity while the on-screen keyboard is up, so the remote's
    // keys go to the keyboard instead of the page.
    public static bool KeyboardVisible { get; set; }

    public static void SetupMain(AndroidWebView view, WebViewHandler handler)
    {
        Main = view;

        // Replaces the default pop-up handler with one that shows sign-in
        // windows over the page. Re-applied on every load because the
        // framework installs its own handler while the view is being created,
        // which would otherwise quietly replace ours and block the pop-up.
        _client ??= new TvChromeClient();
        view.SetWebChromeClient(_client);

        // Give the view our own browser client, which extends the framework's
        // one (so the page's Navigated event still fires) and refuses ad
        // requests. Re-applied on every load because the framework sets its
        // own client while the view is being created.
        _viewClient ??= new TvWebViewClient(handler);
        view.SetWebViewClient(_viewClient);

        Apply(view);
    }

    // Applies to both the main page and any sign-in window it opens.
    public static void Apply(AndroidWebView view)
    {
        var settings = view.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.SetSupportMultipleWindows(true);
        settings.JavaScriptCanOpenWindowsAutomatically = true;

        // Lets the video start on its own WITH sound. Without this, Android only
        // allows a sound-on video to start after a real press, so players start
        // muted - which is why the picture came up but no sound did.
        settings.MediaPlaybackRequiresUserGesture = false;

        // Smoothness on a TV: draw the page on the graphics chip so scrolling
        // and video do not repaint on the slower CPU path.
        view.SetLayerType(Android.Views.LayerType.Hardware, null);

        // Google refuses to sign in from an embedded browser, and the giveaway
        // is the "; wv" marker and "Version/4.0" in the user agent. Drop both
        // so the sign-in window is treated as a normal Chrome browser.
        var userAgent = settings.UserAgentString;
        if (!string.IsNullOrEmpty(userAgent))
        {
            userAgent = userAgent.Replace("; wv", string.Empty)
                                 .Replace("Version/4.0 ", string.Empty);
            settings.UserAgentString = userAgent;
        }

        var cookies = CookieManager.Instance;
        cookies.SetAcceptCookie(true);
        cookies.SetAcceptThirdPartyCookies(view, true);

        InstallAdCleanup(view);
    }

    // Runs the advert cleaner inside EVERY frame, the movie player's own frame
    // included. The QR advert is drawn in there, where the page's own scripts
    // cannot reach. Registered once per view; the script itself guards against
    // running twice.
    private static readonly HashSet<int> CleanupInstalled = new();

    internal static void InstallAdCleanup(AndroidWebView view)
    {
        try
        {
            if (!AndroidX.WebKit.WebViewFeature.IsFeatureSupported(
                    AndroidX.WebKit.WebViewFeature.DocumentStartScript))
                return;

            var id = view.GetHashCode();
            if (!CleanupInstalled.Add(id))
                return;

            // Pop-ups: the player's capability probe must succeed, but the advert
            // windows that ride on the same call must never appear on screen.
            // Installed first so it is in place before any page script runs.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                PopupGuard.Js,
                new[] { "*" });

            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                AdCleanupScript.Js,
                new[] { "*" });

            // Sound: players start their video muted because a browser only
            // lets a silent video start on its own. This unmutes it and starts
            // playback inside the player's own frame.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                AudioAutoplay.Js,
                new[] { "*" });

            // D-pad navigation: the site has no TV navigation of its own, so
            // the page needs a highlight the arrows can move.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                DpadNav.Js,
                new[] { "*" });
        }
        catch
        {
            // An older Android WebView without this feature simply keeps the
            // request blocking, which already stops most of the adverts.
        }
    }

    public static void Focus(AndroidWebView? view)
    {
        if (view is null)
            return;

        view.Focusable = true;
        view.FocusableInTouchMode = true;
        view.RequestFocus();
        view.RequestFocusFromTouch();
    }

    internal static void SetPopup(AndroidWebView? popup)
    {
        Popup = popup;
        if (popup is null)
            KeyboardVisible = false;
    }

    // True while a movie is playing in the fullscreen layer.
    public static bool IsFullscreen => _client?.IsFullscreen == true;

    // Leaves the fullscreen video and drops back to the page.
    public static void ExitFullscreen() => _client?.EndFullscreen();

    public static void ClosePopup()
        => _client?.ClosePopup();
}
