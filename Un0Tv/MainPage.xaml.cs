namespace Un0Tv;

public partial class MainPage : ContentPage
{
    private const string SiteUrl = "https://secondframe.netlify.app/";

    // Whether the switch at startup starts on. The switch itself is remembered
    // on the TV after the first choice.
    private const string BlockerPref = "blocker_enabled";

    private readonly Stack<string> _history = new();
    private string? _currentUrl;
    private bool _siteStarted;

    // True when the ad blocker is switched on. Read by the browser filter
    // through IsBlockerOn, so flipping the switch takes effect at once.
    private static bool _blockerOn = true;

    // The screen lives inside a Shell, so the activity cannot reach it by
    // walking Windows[0].Page. This gives it a direct handle instead.
    public static MainPage? Instance { get; private set; }

    // True while the startup check is on screen, so the activity leaves the
    // remote's keys to Android's own focus movement instead of sending them
    // to a page that has not been loaded yet.
    public bool IsGateVisible => AdGate.IsVisible;

    // Asked for every request by RequestFilter: should this be filtered?
    public static bool IsBlockerOn => _blockerOn;

    public MainPage()
    {
        InitializeComponent();
        Instance = this;

        try
        {
            _blockerOn = Preferences.Get(BlockerPref, true);
        }
        catch
        {
            _blockerOn = true;
        }
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
    // Startup check: the ad blocker switch
    // ---------------------------------------------------------------

    // The block list is part of the app, so there is nothing to install and no
    // permission to grant any more. This only shows the switch, and loads the
    // list from disk on every start so the filter is ready before the site.
    private async void CheckFilter()
    {
        if (_siteStarted)
            return;

        AdGate.IsVisible = true;
        GateProgress.IsVisible = false;
        BtnUpdate.IsVisible = false;
        RefreshBlockerUi();

        bool loaded = false;
        try
        {
            loaded = await Hagezi.LoadAsync();
        }
        catch { }

        if (loaded)
        {
            GateStatus.Text = $"{Hagezi.Count:N0} ad, tracker and malware domains ready.";
            GateHint.Text = "Saved on this TV, so it starts instantly. Use “Update the list” now and then to refresh it.";
        }
        else
        {
            // First start, or the saved list was cleared: offer the download
            // instead of sitting on an empty blocker.
            GateStatus.Text = "The block list has not been downloaded yet.";
            GateHint.Text = "Press the switch to download it once. It is about 30 MB and is kept on this TV afterwards.";
        }

        BtnUpdate.IsVisible = loaded;
        FocusFirstGateButton(BtnBlocker);
    }

    // Draws the switch in whichever state it is in, and says what that means.
    private void RefreshBlockerUi()
    {
        if (_blockerOn)
        {
            BtnBlocker.Text = "AD BLOCKER: ON";
            BtnBlocker.BackgroundColor = Color.FromArgb("#1E8E3E");
            BtnBlocker.TextColor = Colors.White;
        }
        else
        {
            BtnBlocker.Text = "AD BLOCKER: OFF";
            BtnBlocker.BackgroundColor = Color.FromArgb("#4A4A5A");
            BtnBlocker.TextColor = Color.FromArgb("#E8E8F0");
        }
    }

    private void FocusFirstGateButton(VisualElement target)
    {
        // A TV has no pointer, so put the remote's ring on the first action.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            try { target?.Focus(); } catch { }
        });
    }

    // The switch. Turning it on with no list saved downloads the list first;
    // turning it off just stops filtering, which takes effect immediately.
    private async void OnBlockerToggled(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        if (_blockerOn)
        {
            SetBlocker(false);
            GateStatus.Text = "Ad blocking is off. Adverts and pop-ups will show.";
            GateHint.Text = "Press the switch again to turn filtering back on.";
            return;
        }

        SetBlocker(true);

        // Turning it on when nothing is saved yet means downloading first.
        if (!Hagezi.IsLoaded)
            await DownloadListAsync();
        else
            GateStatus.Text = $"{Hagezi.Count:N0} ad, tracker and malware domains ready.";
    }

    private void SetBlocker(bool on)
    {
        _blockerOn = on;
        try
        {
            Preferences.Set(BlockerPref, on);
        }
        catch { }

        RefreshBlockerUi();
    }

    // ---------------------------------------------------------------
    // Downloading the list
    // ---------------------------------------------------------------

    // Set while a download is running, so the buttons cannot start a second
    // one on top of the first.
    private bool _busy;

    private async void OnUpdateClicked(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        await DownloadListAsync();
    }

    private async Task DownloadListAsync()
    {
        _busy = true;
        GateProgress.IsVisible = true;
        BtnBlocker.IsEnabled = false;
        BtnStart.IsEnabled = false;
        BtnUpdate.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(m =>
            {
                GateStatus.Text = m;

                // Five lists, so the bar creeps along as each one arrives.
                GateProgress.Progress = Math.Min(0.9, GateProgress.Progress + 0.18);
            });

            var count = await Hagezi.DownloadAsync(progress);

            GateProgress.Progress = 1;
            GateStatus.Text = $"{count:N0} ad, tracker and malware domains ready.";
            GateHint.Text = "Saved on this TV. Filtering is on.";
            SetBlocker(true);
            BtnUpdate.IsVisible = true;
        }
        catch (Exception ex)
        {
            GateProgress.Progress = 0;
            GateStatus.Text = "The block list could not be downloaded.";
            GateHint.Text = ex.Message + " Press the switch to try again.";
        }
        finally
        {
            _busy = false;
            GateProgress.IsVisible = false;
            BtnBlocker.IsEnabled = true;
            BtnStart.IsEnabled = true;
            BtnUpdate.IsEnabled = true;
        }
    }

    private void OnStartClicked(object? sender, EventArgs e) => StartSite();

    private void StartSite()
    {
        _siteStarted = true;
        AdGate.IsVisible = false;
        Browser.Source = SiteUrl;
    }

    // ---------------------------------------------------------------
    // Moving between the startup buttons with the remote
    // ---------------------------------------------------------------

    // The startup screen is native, not a page, so Android decides this by
    // itself only sometimes. This walks the visible buttons in order, which is
    // what the remote's arrows need on a TV.
    internal void MoveGateFocus(int dx, int dy)
    {
        if (!AdGate.IsVisible)
            return;

        var order = new List<Button> { BtnBlocker };
        if (BtnUpdate.IsVisible) order.Add(BtnUpdate);
        order.Add(BtnStart);

        var current = order.FindIndex(b => b.IsFocused);
        if (current < 0)
        {
            order[0].Focus();
            return;
        }

        // Only up and down matter for a single column; either way the ring
        // steps one place, which is what a viewer expects.
        var step = dy > 0 ? 1 : dy < 0 ? -1 : dx > 0 ? 1 : -1;
        var next = current + step;

        if (next < 0 || next >= order.Count)
            next = step > 0 ? 0 : order.Count - 1;

        try { order[next].Focus(); } catch { }
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
