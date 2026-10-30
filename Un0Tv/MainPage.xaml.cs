namespace Un0Tv;

public partial class MainPage : ContentPage
{
    private const string SiteUrl = "https://secondframe.netlify.app/";

    private readonly Stack<string> _history = new();
    private string? _currentUrl;
    private bool _siteStarted;

    // The screen lives inside a Shell, so the activity cannot reach it by
    // walking Windows[0].Page. This gives it a direct handle instead.
    public static MainPage? Instance { get; private set; }

    // True while the startup check is on screen, so the activity leaves the
    // remote's keys to Android's own focus movement instead of sending them
    // to a page that has not been loaded yet.
    public bool IsGateVisible => AdGate.IsVisible;

    public MainPage()
    {
        InitializeComponent();
        Instance = this;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CheckFilter();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Our pop-up handler has to be (re)installed after the framework has
        // finished wiring the browser view up, otherwise the framework's own
        // handler - which does not allow pop-ups - replaces ours. Deferring a
        // moment puts ours last. AttachNative runs again on every page load,
        // so this is just the early attempt.
        Dispatcher.Dispatch(AttachNative);
    }

    // ---------------------------------------------------------------
    // Startup check for the TV's own ad filter
    // ---------------------------------------------------------------

    // Android does not let one app switch another app's filter on, so the most
    // this check can do is make sure the filter is installed and allowed to
    // run, and take you straight to the right screen when it is not. The site
    // only opens once both are true, or once you choose to carry on without it.
    private void CheckFilter()
    {
        if (_siteStarted)
            return;

#if ANDROID
        var installed = BlockerGate.IsInstalled;
        var allowed = BlockerGate.HasPermission;

        if (installed && allowed)
        {
            StartSite();
            return;
        }

        AdGate.IsVisible = true;
        BtnInstall.IsVisible = !installed;
        BtnAllow.IsVisible = installed && !allowed;

        GateStatus.Text = installed
            ? "The ad filter is installed. Open it and switch its ad and tracker filter on."
            : "Un0Tv works best with the free DNS ad blocker (Repel) installed, so adverts are filtered on this TV.";

        GateHint.Text = installed
            ? "Press “Check again” once the switch is on. You can also carry on without it."
            : "Press “Install ad filter” to open Google Play, then come back here.";

        FocusFirstGateButton(installed ? BtnAllow : BtnInstall);
#else
        StartSite();
#endif
    }

    private void FocusFirstGateButton(VisualElement target)
    {
        // A TV has no pointer, so put the remote's ring on the first action.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            try { target?.Focus(); } catch { }
        });
    }

    private void OnInstallClicked(object? sender, EventArgs e)
    {
#if ANDROID
        BlockerGate.OpenPlayStore();
#endif
    }

    private void OnAllowClicked(object? sender, EventArgs e)
    {
#if ANDROID
        // Opens the filter's own screen so its ad and tracker switch can be
        // turned on. Android will not let this app flip that switch for you.
        BlockerGate.OpenApp();
#endif
    }

    private void OnRecheckClicked(object? sender, EventArgs e) => CheckFilter();

    private void OnSkipClicked(object? sender, EventArgs e) => StartSite();

    private void StartSite()
    {
        _siteStarted = true;
        AdGate.IsVisible = false;
        Browser.Source = SiteUrl;
    }

    // ---------------------------------------------------------------
    // The browser and the remote
    // ---------------------------------------------------------------

    // Hands the native browser view to the Android side, so it can allow
    // pop-ups (the Google sign-in window) and route the remote's keys.
    private void AttachNative()
    {
#if ANDROID
        if (Browser.Handler is Microsoft.Maui.Handlers.WebViewHandler handler
            && handler.PlatformView is Android.Webkit.WebView native)
            TvWebView.SetupMain(native, handler);
#endif
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (_currentUrl is not null)
            _history.Push(_currentUrl);

        _currentUrl = e.Url;
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            // Site failed to load — show a bare error so you're not staring at a black screen
            Browser.Source = new HtmlWebViewSource
            {
                Html = $@"<html><body style='background:#101014;color:#E8E8F0;
                         font-family:sans-serif;display:flex;align-items:center;
                         justify-content:center;height:100vh;margin:0'>
                         <div style='text-align:center'>
                           <h2>couldn't reach secondframe</h2>
                           <p style='color:#8A8AA0'>{e.Result}</p>
                         </div></body></html>"
            };
            return;
        }

        // A fresh page loses our helpers, so put them back after every load.
        // Doing the native setup here too guarantees our pop-up handler is the
        // one in place, because this runs well after the framework has set its
        // own up.
        AttachNative();
        RunScript(PageScripts.AutoPlay);

        // Some sites render their buttons slightly after the page reports "loaded",
        // so inject once more a moment later as well. The call is harmless if the
        // helper is already there.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
        {
            RunScript(PageScripts.AutoPlay);
        });
    }

    // Called from MainActivity for the remote's arrows and its centre button.
    // The highlight itself lives in the page (DpadNav), because only the page
    // knows what can be pressed.
    public void MoveFocus(int dx, int dy)
        => RunScript($"window.__un0tvNav&&window.__un0tvNav.move({dx},{dy});");

    public void ActivateFocus()
        => RunScript("window.__un0tvNav&&window.__un0tvNav.activate();");

    // Called from MainActivity on Back button
    public bool HandleBack()
    {
        if (_history.Count > 0)
        {
            var prev = _history.Pop();
            Browser.Source = prev;
            return true; // handled, don't exit
        }
        return false; // let the system exit
    }

    // Run JavaScript inside the page. On Android we talk to the native WebView
    // directly, which is the most reliable way to inject a multi-line script.
    private void RunScript(string js)
    {
#if ANDROID
        if (Browser.Handler?.PlatformView is Android.Webkit.WebView native)
        {
            native.EvaluateJavascript(js, null);
            return;
        }
#endif
        _ = Browser.EvaluateJavaScriptAsync(js);
    }
}
