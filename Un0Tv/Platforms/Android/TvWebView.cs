using Android.Views.InputMethods;
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
    private static ProgressBridge? _progress;

    public static AndroidWebView? Main { get; private set; }
    public static AndroidWebView? Popup { get; private set; }

    public static bool IsPopupOpen => Popup is not null;

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

        // Lets the site's page hand its watch progress to the app (see ProgressBridge).
        // Only the main view gets it, not sign-in pop-ups.
        view.AddJavascriptInterface(_progress ??= new ProgressBridge(), "Un0TvNative");

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

        // No white paper background behind the page: while the site paints or
        // swaps pages, a white view would flash through the gaps. Black matches
        // the app, so nothing bright ever shows behind it.
        view.SetBackgroundColor(Android.Graphics.Color.Black);

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

        // The cookie manager is missing on a very few devices. Without it the
        // page still works, it just forgets who is signed in.
        var cookies = CookieManager.Instance;
        if (cookies is not null)
        {
            cookies.SetAcceptCookie(true);
            cookies.SetAcceptThirdPartyCookies(view, true);
        }

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

            // Movie player controls: play, pause, forward and back from the
            // remote's media keys, sent into the player's frame.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                PlayerControl.Js,
                new[] { "*" });

            // The movie's own video element, driven directly in EVERY frame.
            // The player builds its video with a blob: address, which only
            // exists inside the player's frame - so the commands go to the
            // video element there instead of through the site's API.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                VideoControl.Js,
                new[] { "*" });

            // D-pad navigation: the site has no TV navigation of its own, so
            // the page needs a highlight the arrows can move.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                DpadNav.Js,
                new[] { "*" });

            // Typing into the site's search box from the app's on-screen
            // keyboard: the page remembers which box the remote is on.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                FieldInput.Js,
                new[] { "*" });

            // Page route reporter: tells the app which site page is on screen.
            AndroidX.WebKit.WebViewCompat.AddDocumentStartJavaScript(
                view,
                PageRoute.Js,
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

    // Raises the TV's own keyboard over the site's search box.
    //
    // The page has already put the caret in the real input; this only asks
    // Android to show its keys. Chromium shows them by itself only after a real
    // touch, and this app always puts the caret there with a script - which is
    // why nothing came up before.
    public static void ShowKeyboard()
    {
        var view = Popup ?? Main;
        if (view is null)
            return;

        view.Focusable = true;
        view.FocusableInTouchMode = true;
        view.RequestFocus();

        var imm = InputMethod();
        if (imm is null)
            return;

        // Android's own flag for "show the keys now, even though the press did
        // not come from a touch on the box".
        imm.ShowSoftInput(view, ShowFlags.Implicit);

        // Many TVs have no keyboard at all, and Android only shows its own
        // after a real touch. So the app watches: if no keys appeared, it
        // raises its own letter pad instead, which the D-pad always reaches.
        _ = Task.Run(async () =>
        {
            await Task.Delay(600);
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!MainActivity.SoftKeysUp())
                    KeyPad.Show();
            });
        });
    }

    // Puts the keys away again. Back does this as well, so the viewer can never
    // be left with keys covering the page and no way out.
    public static void HideKeyboard()
    {
        var view = Popup ?? Main;
        if (view is null)
            return;

        InputMethod()?.HideSoftInputFromWindow(view.WindowToken, HideSoftInputFlags.None);
    }

    private static Android.Views.InputMethods.InputMethodManager? InputMethod()
        => Android.App.Application.Context
                  .GetSystemService(Android.Content.Context.InputMethodService)
           as Android.Views.InputMethods.InputMethodManager;

    internal static void SetPopup(AndroidWebView? popup)
        => Popup = popup;

    // True while a movie is playing in the fullscreen layer.
    public static bool IsFullscreen => _client?.IsFullscreen == true;

    // Leaves the fullscreen video and drops back to the page.
    public static void ExitFullscreen() => _client?.EndFullscreen();

    // True while a movie is on screen, as reported by the player's own frame.
    // The Left key opens the command panel only then, because off a movie the
    // Left key must keep moving the highlight.
    public static bool MoviePlaying { get; private set; }

    // The site's current route (path + query). Null until the first report.
    public static string? CurrentRoute { get; private set; }

    // True when the current route looks like a movie player page.
    public static bool OnMoviePage
        => CurrentRoute is not null
          && (CurrentRoute.Contains("/watch")
              || CurrentRoute.Contains("/play")
              || CurrentRoute.Contains("/movie")
              || CurrentRoute.Contains("/episode"));

    internal static void SetMoviePlaying(bool on) => MoviePlaying = on;

    // Called by PageRoute through Un0TvNative.route()
    internal static void SetRoute(string path)
    {
        CurrentRoute = path;
        System.Diagnostics.Debug.WriteLine("[Un0Tv] Route: " + path);
    }

    // Sends one movie command (play, pause, toggle, forward or back) into the
    // player's own frame. The action is always one of those fixed words.
    //
    // The site's own PostMessage API is not used for this any more: the movie
    // is built with a blob: address inside the player's frame, so only that
    // frame can drive it. VideoControl sits in every frame, acts on the video
    // element, and passes the command on to the frames below it - so a player
    // nested two frames deep still hears it.
    public static void PlayerCommand(string action)
    {
        var view = Main;
        if (view is null)
            return;

        var js = "(function(){var m={__un0tvCmd:'" + action + "',__un0tvHop:1};"
               + "try{var f=document.querySelectorAll('iframe');"
               + "for(var i=0;i<f.length;i++){f[i].contentWindow.postMessage(m,'*');}}catch(e){}"
               + "try{if(window.__un0tvPlayer&&window.__un0tvPlayer." + action + ")"
               + "window.__un0tvPlayer." + action + "();}catch(e){}})();";

        view.EvaluateJavascript(js, null);
    }

    // Takes the page's cyan highlight away while the app's own panels are up.
    public static void ClearHighlight()
        => Eval("if(window.__un0tvNav){window.__un0tvNav.clear();}");

    // Types one letter into the search box the page has in focus. The box is
    // asked for again first, because a movie page may have moved under it while
    // the pad was being used.
    public static void TypeText(string text)
        => Eval("if(window.__un0tvNav){window.__un0tvNav.refocus();}"
                + "if(window.__un0tvField){window.__un0tvField.insert('"
                + text.Replace("\\", "\\\\").Replace("'", "\\'") + "');}");

    public static void EraseText()
        => Eval("if(window.__un0tvField){window.__un0tvField.backspace();}");

    public static void SubmitField()
        => Eval("if(window.__un0tvField){window.__un0tvField.submit();}");

    // Steps back one page inside the site, used by the panel's Back button.
    public static void GoBackPage()
    {
        var view = Main;
        try
        {
            if (view is not null && view.CanGoBack())
                view.GoBack();
        }
        catch { }
    }

    private static void Eval(string js)
    {
        try { Main?.EvaluateJavascript(js, null); } catch { }
    }

    public static void ClosePopup()
        => _client?.ClosePopup();
}
