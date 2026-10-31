using System.Collections.ObjectModel;
using Microsoft.Maui.Dispatching;

namespace Un0Tv;

public partial class MainPage : ContentPage
{
    internal const string SiteUrl = "https://secondframe.netlify.app/";

    // What the switches were set to last time. Kept on the TV so a viewer does
    // not have to set them again on every start.
    private const string FilterPref = "blocker_enabled";
    private const string RowsPref = "activity_blocked_only";

    private readonly Stack<string> _history = new();
    private string? _currentUrl;
    private bool _siteStarted;

    // True when the filter layer is switched on. Read by the browser filter
    // through IsBlockerOn, so flipping the switch takes effect at once.
    private static bool _filterOn = true;

    // Which rows the Activity list shows: everything, or only what was refused.
    private bool _blockedOnly;

    // What the Activity list draws. Kept separate from the full log so the All /
    // Blocked tab can hide rows without losing them, and updated in place so the
    // list does not flicker once a second.
    private readonly ObservableCollection<LogEntry> _visibleRows = new();

    // The heartbeat that keeps the figures and the "seconds ago" text honest
    // while the home screen or the Activity list is open.
    private IDispatcherTimer? _beat;

    // The screen lives inside a Shell, so the activity cannot reach it by
    // walking Windows[0].Page. This gives it a direct handle instead.
    public static MainPage? Instance { get; private set; }

    // True while a screen of ours is over the site, so the activity leaves the
    // remote's keys to our own focus movement instead of sending them to a page
    // that has not loaded.
    public bool IsGateVisible => HomePanel.IsVisible || ActivityPanel.IsVisible || DnsPanel.IsVisible;

    // Asked for every request by RequestFilter: should this be filtered?
    public static bool IsBlockerOn => _filterOn;

    public MainPage()
    {
        InitializeComponent();
        Instance = this;

        try
        {
            _filterOn = Preferences.Get(FilterPref, true);
            _blockedOnly = Preferences.Get(RowsPref, false);
            AdStats.Load();
        }
        catch
        {
            _filterOn = true;
        }

        ActivityList.ItemsSource = _visibleRows;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ShowHome();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        AdStats.Save();
        StopBeat();
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
    // The home screen
    // ---------------------------------------------------------------

    // Shows the protection screen and loads the saved block list, so the filter
    // is ready before the site asks for anything.
    private async void ShowHome()
    {
        if (_siteStarted)
            return;

        // Always on: the DNS filter and Private DNS are switched on here, with
        // no question asked of the viewer.
        EnsureVpnRunning();
        _dnsApplied = TryApplyPrivateDns(DnsPresets.Current.Host);

        ActivityPanel.IsVisible = false;
        DnsPanel.IsVisible = false;
        HomePanel.IsVisible = true;
        ListProgress.IsVisible = false;
        DrawLayers();
        DrawContinue();

        StartBeat();

        bool loaded = false;
        try
        {
            loaded = await Hagezi.LoadAsync();
        }
        catch { }

        DrawLayers(loaded);
        FocusFirst(BtnFilter);
    }

    // Draws the shield, the figures and the three layer rows from the real
    // state of the app - nothing here is a decoration.
    private void DrawLayers(bool? loadedOverride = null)
    {
        var listOn = loadedOverride ?? Hagezi.IsLoaded;

        // ---- The shield ----
        var protectedNow = _filterOn;

        ShieldGlyph.Text = protectedNow ? "\u2713" : "\u2715";
        ShieldGlyph.TextColor = Color.FromArgb(protectedNow ? "#34D399" : "#F0736A");
        ShieldRing.Stroke = Color.FromArgb(protectedNow ? "#255C3A" : "#5C2525");
        ShieldState.Text = protectedNow ? "Protected" : "Not protected";
        ShieldState.TextColor = Color.FromArgb(protectedNow ? "#FFFFFF" : "#F0736A");

        var layersOn = (protectedNow ? 1 : 0) + (listOn ? 1 : 0) + 1;
        _ = layersOn;

        // ---- Layer 1: the filter ----
        BtnFilter.Text = _filterOn ? "ON" : "OFF";
        BtnFilter.BackgroundColor = Color.FromArgb(_filterOn ? "#34D399" : "#3A3A4E");
        BtnFilter.TextColor = Color.FromArgb(_filterOn ? "#08210F" : "#C9C9DE");
        LayerFilterInfo.Text = _filterOn
            ? "Every request checked before it loads"
            : "Off - adverts and pop-ups will show";

        // ---- Layer 2: the public list ----
        if (listOn)
        {
            BtnList.Text = "Update";
            BtnList.BackgroundColor = Color.FromArgb("#162036");
            BtnList.TextColor = Color.FromArgb("#E6F1FF");

            var when = Hagezi.SavedAt;
            LayerListInfo.Text = when is null
                ? $"{Hagezi.Count:N0} domains loaded"
                : $"{Hagezi.Count:N0} domains \u00b7 saved {when.Value.LocalDateTime:dd MMM}";
        }
        else
        {
            BtnList.Text = "Get";
            BtnList.BackgroundColor = Color.FromArgb("#8B5CF6");
            BtnList.TextColor = Colors.White;
            LayerListInfo.Text = "Not downloaded yet \u00b7 tap Get to fetch it";
        }

        // ---- Layer 3: Private DNS ----
        BtnDns.Text = "Open";
        BtnDns.BackgroundColor = Color.FromArgb("#162036");
        BtnDns.TextColor = Color.FromArgb("#E6F1FF");
        LayerDnsInfo.Text = _dnsApplied
            ? $"On \u00b7 {DnsPresets.Current.Host}"
            : "Off \u00b7 press Open to turn it on";

        HomeHint.Text = _filterOn
            ? "Arrows move, OK presses. Filtering is on."
            : "Filtering is off. Open the first layer to block adverts.";

        DrawActivityHeader();
    }

    private void DrawActivityHeader()
    {
        BtnRowsFilter.Text = _blockedOnly ? "Blocked" : "All";
        BtnRowsFilter.BackgroundColor = Color.FromArgb(_blockedOnly ? "#3A2E5C" : "#162036");
        BtnRowsFilter.TextColor = Color.FromArgb(_blockedOnly ? "#C7B8FF" : "#E6F1FF");

        AdStats.Apply(_blockedOnly, _visibleRows);
        ActivityEmpty.IsVisible = _visibleRows.Count == 0;
    }

    private void StartBeat()
    {
        if (_beat is not null)
            return;

        _beat = Dispatcher.CreateTimer();
        _beat.Interval = TimeSpan.FromSeconds(1);
        _beat.Tick += (_, _) =>
        {
            // Only while one of our screens is up: the figures are for the
            // viewer, not for the page.
            if (!IsGateVisible)
                return;

            AdStats.Drain();

            if (ActivityPanel.IsVisible)
                DrawActivityHeader();
        };

        _beat.Start();
    }

    private void StopBeat()
    {
        _beat?.Stop();
        _beat = null;
        AdStats.Save();
    }

    private void FocusFirst(VisualElement target)
    {
        // A TV has no pointer, so put the remote's ring on the first action.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            try { target?.Focus(); } catch { }
        });
    }

    // ---------------------------------------------------------------
    // The switches and the buttons
    // ---------------------------------------------------------------

    // Layer 1. Turning it on with no list saved fetches the list first, so the
    // shield never says "Protected" while the filter has nothing to match.
    private async void OnFilterToggled(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        _filterOn = !_filterOn;
        try { Preferences.Set(FilterPref, _filterOn); } catch { }

        DrawLayers();

        if (_filterOn && !Hagezi.IsLoaded)
            await FetchListAsync();
    }

    // Layer 2. Fetches the five HaGeZi lists (or refreshes them).
    private async void OnListClicked(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        await FetchListAsync();
    }

    private bool _busy;

    // True once the chosen Private DNS name is switched on.
    private bool _dnsApplied;

    // The VPN filter is asked for once per run, not on every visit to the home screen.
    private static bool _vpnAutoTried;

    // Starts the whole-TV DNS filter if it is not running yet. Android may show
    // its own one-time permission box; that box belongs to the system.
    private static void EnsureVpnRunning()
    {
        if (DnsSettings.VpnRunning || _vpnAutoTried)
            return;

        _vpnAutoTried = true;
#if ANDROID
        MainActivity.Current?.StartVpnFlow();
#endif
    }

    // Switches the TV's Private DNS to the given name, with no question asked.
    // Android allows this only to apps granted the secure-settings permission;
    // for any other app the change is refused and this returns false.
    private static bool TryApplyPrivateDns(string host)
    {
#if ANDROID
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            if (resolver is null)
                return false;

            var modeOk = Android.Provider.Settings.Global.PutString(resolver, "private_dns_mode", "hostname");
            var hostOk = Android.Provider.Settings.Global.PutString(resolver, "private_dns_specifier", host);
            return modeOk && hostOk;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private async Task FetchListAsync()
    {
        _busy = true;
        ListProgress.IsVisible = true;
        ListProgress.Progress = 0;
        BtnFilter.IsEnabled = false;
        BtnList.IsEnabled = false;
        BtnStart.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(m =>
            {
                LayerListInfo.Text = m;
                ListProgress.Progress = Math.Min(0.9, ListProgress.Progress + 0.18);
            });

            var count = await Hagezi.DownloadAsync(progress);

            ListProgress.Progress = 1;
            _filterOn = true;
            try { Preferences.Set(FilterPref, true); } catch { }
            DrawLayers(true);
            LayerListInfo.Text = $"{count:N0} domains ready";
        }
        catch (Exception ex)
        {
            ListProgress.Progress = 0;
            LayerListInfo.Text = "Could not download. Tap Get to try again.";
            HomeHint.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            ListProgress.IsVisible = false;
            BtnFilter.IsEnabled = true;
            BtnList.IsEnabled = true;
            BtnStart.IsEnabled = true;
        }
    }

    // Layer 3. Turns Private DNS on for the chosen name with no question to the
    // viewer. Only if Android refuses the switch does it open the TV's network
    // page, so the viewer can flip it there in one press.
    private void OnOpenDnsSettingsClicked(object? sender, EventArgs e)
    {
        var host = DnsPresets.Current.Host;
        _dnsApplied = TryApplyPrivateDns(host);

        if (_dnsApplied)
        {
            DnsPresetInfo.Text = $"Private DNS is on: {host}";
            DrawLayers();
            return;
        }

#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionSettings);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
        }
        catch { }
#endif
        DnsPresetInfo.Text = $"Android blocked the switch. In the page that opens, set Private DNS to {host}.";
    }

    // Opens the presets screen from the Private DNS row on the home screen.
    private void OnDnsPanelClicked(object? sender, EventArgs e)
    {
        HomePanel.IsVisible = false;
        DnsPanel.IsVisible = true;
        DrawPresets();
        DrawVpn();
        FocusFirst(BtnDnsBack);
    }

    private void OnDnsBackClicked(object? sender, EventArgs e)
    {
        DnsPanel.IsVisible = false;
        ShowHome();
        FocusFirst(BtnDns);
    }

    private readonly List<Button> _presetButtons = new();

    // The Continue watching cards, rebuilt each time the home screen opens.
    private readonly List<Button> _continueButtons = new();

    // Draws the Continue watching row from the saved progress. The row always
    // shows, so the viewer can see where their titles will appear.
    private void DrawContinue()
    {
        var items = WatchProgress.Recent(3);

        ContinueList.Children.Clear();
        _continueButtons.Clear();

        if (items.Count == 0)
        {
            ContinueList.Children.Add(new Label
            {
                Text = "Nothing yet - start a movie and it shows up here.",
                FontFamily = "Segoe UI",
                FontSize = 13,
                TextColor = Color.FromArgb("#6C6C84"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(2, 8, 0, 0)
            });
            return;
        }

        foreach (var item in items)
        {
            var picked = item;
            var button = new Button
            {
                Text = $"{picked.Label}\n{picked.Progress}",
                FontFamily = "Segoe UI",
                FontSize = 13,
                CornerRadius = 4,
                Padding = new Thickness(14, 6),
                WidthRequest = 158,
                HeightRequest = 74,
                BackgroundColor = Color.FromArgb("#0F1628"),
                TextColor = Color.FromArgb("#EDEDF7")
            };
            button.Clicked += (_, _) => ResumeTitle(picked);
            _continueButtons.Add(button);
            ContinueList.Children.Add(button);
        }
    }

    // Opens the page the title was watched on. The player then resumes from the
    // saved spot once it reports its time.
    private void ResumeTitle(WatchItem item)
    {
        _siteStarted = true;
        AdStats.IsWatched = false;
        HomePanel.IsVisible = false;
        ActivityPanel.IsVisible = false;
        StopBeat();
        AttachNative();
        Browser.Source = string.IsNullOrEmpty(item.Url) ? SiteUrl : item.Url;
    }

    // Builds one button per preset, once, above the two fixed buttons.
    private void BuildPresets()
    {
        if (_presetButtons.Count > 0)
            return;

        for (var i = 0; i < DnsPresets.All.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                FontFamily = "Segoe UI",
                FontSize = 17,
                CornerRadius = 4,
                BorderWidth = 0,
                Padding = new Thickness(22, 12),
                HorizontalOptions = LayoutOptions.Start,
                BackgroundColor = Color.FromArgb("#0F1628"),
                TextColor = Color.FromArgb("#EDEDF7")
            };
            button.Clicked += (_, _) => PickPreset(index);
            _presetButtons.Add(button);
            DnsPresetList.Children.Insert(i, button);
        }
    }

    private void DrawPresets()
    {
        BuildPresets();

        for (var i = 0; i < _presetButtons.Count; i++)
        {
            var mark = i == DnsPresets.SelectedIndex ? "\u2713  " : "";
            _presetButtons[i].Text = mark + DnsPresets.All[i].Name;
        }

        var cur = DnsPresets.Current;
        DnsPresetInfo.Text = $"Chosen: {cur.Host} \u00b7 {cur.Note}";
    }

    // Chooses a preset and switches Private DNS to it straight away.
    private void PickPreset(int index)
    {
        DnsPresets.SelectedIndex = index;
        DrawPresets();

        var host = DnsPresets.Current.Host;
        _dnsApplied = TryApplyPrivateDns(host);

        DnsPresetInfo.Text = _dnsApplied
            ? $"Private DNS is on: {host}"
            : $"{host} chosen. Press Turn on Private DNS to switch it on.";
    }

    // Turns the whole-TV VPN filter on or off. Android asks for permission the
    // first time; the answer comes back to the main activity.
    private void OnVpnClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (DnsSettings.VpnRunning)
            MainActivity.Current?.StopVpnFlow();
        else
            MainActivity.Current?.StartVpnFlow();
#endif
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), RefreshVpn);
    }

    public void RefreshVpn() => DrawVpn();

    private void DrawVpn()
    {
        BtnVpn.Text = DnsSettings.VpnRunning
            ? "Stop the VPN filter"
            : "Filter every app on this TV (VPN)";
    }

    private void OnActivityClicked(object? sender, EventArgs e)
    {
        ShowActivity();
    }

    // The Activity list. Reached with Back from the home screen, which keeps
    // that screen down to the one button a viewer needs.
    internal void ShowActivity()
    {
        if (!HomePanel.IsVisible && !ActivityPanel.IsVisible && !DnsPanel.IsVisible)
            return;

        HomePanel.IsVisible = false;
        DnsPanel.IsVisible = false;
        ActivityPanel.IsVisible = true;

        // Rows are only collected while a screen is looking at them.
        AdStats.IsWatched = true;
        AdStats.Drain();
        DrawActivityHeader();

        FocusFirst(BtnBack);
    }

    private void OnBackToHomeClicked(object? sender, EventArgs e)
    {
        ActivityPanel.IsVisible = false;
        AdStats.IsWatched = false;
        ShowHome();
        FocusFirst(BtnStart);
    }

    // All / Blocked, the two tabs Repel puts above its list.
    private void OnRowsFilterClicked(object? sender, EventArgs e)
    {
        _blockedOnly = !_blockedOnly;
        try { Preferences.Set(RowsPref, _blockedOnly); } catch { }

        DrawActivityHeader();
    }

    private void OnStartClicked(object? sender, EventArgs e) => StartSite();

    private void StartSite()
    {
        _siteStarted = true;
        AdStats.IsWatched = false;
        HomePanel.IsVisible = false;
        ActivityPanel.IsVisible = false;
        StopBeat();
        AttachNative();
        Browser.Source = SiteUrl;
    }

    // ---------------------------------------------------------------
    // Moving between the screen's buttons with the remote
    // ---------------------------------------------------------------

    // These screens are native, not pages, so Android decides this by itself
    // only sometimes. This walks the visible buttons in order, which is what
    // the remote's arrows need on a TV.
    internal void MoveGateFocus(int dx, int dy)
    {
        if (!IsGateVisible)
            return;

        var order = new List<VisualElement>();

        if (HomePanel.IsVisible)
        {
            // The home screen keeps just the one button. The Activity list is
            // reached with Back, so a viewer has one obvious thing to press.
            order.Add(BtnFilter);
            order.Add(BtnList);
            order.Add(BtnDns);
            foreach (var b in _continueButtons)
                order.Add(b);
            order.Add(BtnStart);
        }
        else if (DnsPanel.IsVisible)
        {
            foreach (var b in _presetButtons)
                order.Add(b);
            order.Add(BtnVpn);
            order.Add(BtnOpenDns);
            order.Add(BtnDnsBack);
        }
        else
        {
            order.Add(BtnBack);
            order.Add(BtnRowsFilter);
        }

        var current = order.FindIndex(b => b.IsFocused);
        if (current < 0)
        {
            try { order[0].Focus(); } catch { }
            return;
        }

        // Mostly one column (or one row of two), so either key steps once,
        // which is what a viewer expects.
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
            // Site failed to load â€” show a bare error so you're not staring at a black screen
            Browser.Source = new HtmlWebViewSource
            {
                Html = $@"<html><body style='background:radial-gradient(circle at top,#14204A,#05070D 70%);color:#E6F1FF;
                         font-family:sans-serif;display:flex;align-items:center;
                         justify-content:center;height:100vh;margin:0'>
                         <div style='text-align:center'>
                           <h2 style='color:#67E8F9;letter-spacing:1px'>couldn't reach secondframe</h2>
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
#if ANDROID
        Overlays.ResetForPage();
#endif
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

    // Runs a script in the page for the Android side (the on-screen keyboard
    // types into the site's own search box this way).
    public void RunInPage(string js) => RunScript(js);

    // Called from MainActivity on Back button
    public bool HandleBack()
    {
        // On one of our screens, Back returns to the home screen first.
        if (ActivityPanel.IsVisible)
        {
            OnBackToHomeClicked(this, EventArgs.Empty);
            return true;
        }

        if (DnsPanel.IsVisible)
        {
            OnDnsBackClicked(this, EventArgs.Empty);
            return true;
        }

        if (HomePanel.IsVisible)
        {
            // Back is the way to the Activity list. It never exits the app from
            // here, which is what used to leave a blank screen behind it.
            ShowActivity();
            return true;
        }

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
