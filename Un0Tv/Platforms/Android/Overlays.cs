using Android.App;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidButton = Android.Widget.Button;
using AndroidColor = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
namespace Un0Tv;

// The movie control panel that sits over the site. A TV has no touch screen,
// so it is drawn by the app as plain Android views above the browser.
//
//   Player panel - opened with Left on the remote while a movie is playing, or
//                  while a movie fills the screen. Pause, Play, 10 seconds back,
//                  10 seconds forward, and Close. It hides by itself after a
//                  few seconds, and Left closes it again.
//
// Every call is made on the screen's own thread, and a missing screen or a
// closed browser simply means "nothing to draw yet" - never an exception.
internal static class Overlays
{
    private static Activity? Act => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

    public static bool PlayerOpen { get; private set; }

    // Set from the page script: true while a movie is actually playing. The
    // remote's Left key opens the panel only then, so moving around the site
    // with Left keeps working as before.
    public static bool MediaActive { get; set; }

    private static int Dp(int value)
    {
        var density = Act?.Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)(value * density);
    }

    // "#RRGGBB" or "#AARRGGBB".
    private static AndroidColor C(string hex) => AndroidColor.ParseColor(hex);

    private static ColorStateList Cs(string hex) => ColorStateList.ValueOf(C(hex));

    private static void Paint(AndroidButton button, string text, string back)
    {
        button.SetTextColor(Cs(text));
        button.SetBackgroundColor(C(back));
    }

    // ---------------------------------------------------------------
    // The movie control panel
    // ---------------------------------------------------------------

    private static FrameLayout? _playerHost;
    private static readonly List<AndroidButton> _playerButtons = new();
    private static readonly Handler _timer = new(Looper.MainLooper!);
    private static Action? _pendingHide;

    public static void OpenPlayer()
    {
        var act = Act;
        if (act is null)
            return;

        act.RunOnUiThread(() =>
        {
            try
            {
                BuildPlayer(act);
                if (_playerHost is null)
                    return;

                _playerHost.Visibility = ViewStates.Visible;
                PlayerOpen = true;

                // Put the ring on the first button, so the arrows act on the
                // panel rather than the page underneath.
                if (_playerButtons.Count > 0)
                {
                    _playerButtons[0].RequestFocus();
                    Paint(_playerButtons[0], "#FFFFFF", "#0E7490");
                }

                ArmAutoHide();
            }
            catch { }
        });
    }

    private static void BuildPlayer(Activity act)
    {
        if (_playerHost is not null && _playerHost.IsAttachedToWindow)
            return;

        var host = new FrameLayout(act);

        var column = new LinearLayout(act) { Orientation = Orientation.Vertical };
        column.SetGravity(GravityFlags.CenterVertical | GravityFlags.Start);
        column.SetPadding(Dp(24), 0, 0, 0);

        var card = new LinearLayout(act) { Orientation = Orientation.Vertical };
        card.SetBackgroundColor(C("#F0070B16"));
        card.SetPadding(Dp(14), Dp(16), Dp(14), Dp(16));

        var title = new TextView(act) { Text = "Player" };
        title.SetTextColor(Cs("#7FE3F5"));
        title.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        title.SetPadding(Dp(6), 0, 0, Dp(10));
        card.AddView(title);

        _playerButtons.Clear();
        AddPlayerButton(act, card, "Pause", "pause");
        AddPlayerButton(act, card, "Play", "play");
        AddPlayerButton(act, card, "-10s", "back");
        AddPlayerButton(act, card, "+10s", "forward");
        AddCloseButton(act, card);

        column.AddView(card, new LinearLayout.LayoutParams(Dp(210), ViewGroup.LayoutParams.WrapContent));
        host.AddView(column, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        host.Visibility = ViewStates.Gone;

        if (act.Window?.DecorView is ViewGroup decor)
            decor.AddView(host, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        _playerHost = host;
    }

    private static void AddPlayerButton(Activity act, LinearLayout card, string text, string action)
    {
        var button = new AndroidButton(act) { Text = text, Focusable = true, FocusableInTouchMode = true };
        button.SetTextSize(Android.Util.ComplexUnitType.Sp, 17);
        button.SetPadding(0, 0, 0, 0);
        Paint(button, "#E6F1FF", "#162036");

        var layout = new LinearLayout.LayoutParams(Dp(180), Dp(64));
        layout.BottomMargin = Dp(10);
        card.AddView(button, layout);

        button.FocusChange += (_, e) =>
        {
            if (e.HasFocus) Paint(button, "#FFFFFF", "#0E7490");
            else Paint(button, "#E6F1FF", "#162036");
        };

        // A press sends the command and closes the panel: the viewer is
        // watching the film, not the panel.
        button.Click += (_, _) =>
        {
            TvWebView.PlayerCommand(action);
            ClosePlayer();
        };

        _playerButtons.Add(button);
    }

    private static void AddCloseButton(Activity act, LinearLayout card)
    {
        var button = new AndroidButton(act) { Text = "Close", Focusable = true, FocusableInTouchMode = true };
        button.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        button.SetPadding(0, 0, 0, 0);
        Paint(button, "#9AB0C9", "#101827");

        var layout = new LinearLayout.LayoutParams(Dp(180), Dp(56));
        card.AddView(button, layout);

        button.FocusChange += (_, e) =>
        {
            if (e.HasFocus) Paint(button, "#FFFFFF", "#0E7490");
            else Paint(button, "#9AB0C9", "#101827");
        };

        button.Click += (_, _) => ClosePlayer();

        _playerButtons.Add(button);
    }

    public static void ClosePlayer()
    {
        PlayerOpen = false;
        CancelAutoHide();
        _playerHost?.Visibility = ViewStates.Gone;
        TvWebView.Focus(TvWebView.Popup ?? TvWebView.Main);
    }

    private static void ArmAutoHide()
    {
        CancelAutoHide();
        _pendingHide = ClosePlayer;
        _timer.PostDelayed(_pendingHide, 9000);
    }

    private static void CancelAutoHide()
    {
        if (_pendingHide is not null)
            _timer.RemoveCallbacks(_pendingHide);
        _pendingHide = null;
    }

    // Any key on the panel keeps it up a little longer.
    public static void TouchPlayer() => ArmAutoHide();

    // A new page resets the panel state and the movie flag.
    public static void ResetForPage()
    {
        MediaActive = false;
        if (PlayerOpen)
            ClosePlayer();
    }
}
