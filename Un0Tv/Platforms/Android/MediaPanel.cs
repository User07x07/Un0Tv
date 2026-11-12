using Android.App;
using Android.Views;
using Android.Widget;
using AndroidColor = Android.Graphics.Color;

namespace Un0Tv;

// The small command panel the remote's Left key opens over a movie: Home,
// Beginning, Back 10s, Play/Pause, Forward 10s. A Next button joins them for a
// TV episode at 90% watched.
//
// The buttons are real Android views, so the D-pad always reaches them - the
// site's own player controls sit inside a player frame the app cannot focus,
// which is why the remote's buttons did nothing there.
//
// It closes itself after a few seconds without a press, so the picture is
// never left with a bar on it.
internal static class MediaPanel
{
    private static FrameLayout? _root;
    private static Android.Widget.Button? _first;
    private static Android.Widget.Button? _next;
    private static Android.Widget.Button? _beginning;
    private static Android.Widget.Button? _forward;
    private static Android.Widget.Button? _backward;
    private static int _generation;

    public static bool IsOpen => _root is not null && _root.Visibility == ViewStates.Visible;

    private static Activity? Act => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

    private static int Dp(float v) => (int)(v * (Act?.Resources?.DisplayMetrics?.Density ?? 1f) + 0.5f);

    public static void Show()
    {
        var activity = Act;
        if (activity?.Window?.DecorView is not ViewGroup decor)
            return;

        // Only open on a movie page - the Left key must keep moving the
        // highlight on the dashboard and other pages.
        if (!TvWebView.OnMoviePage)
            return;

        if (_root is null)
            Build(activity);

        var root = _root!;
        if (root.Parent is null)
            decor.AddView(root, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        root.Visibility = ViewStates.Visible;
        root.BringToFront();

        // The page's own highlight goes away, so it is clear the remote is on
        // the panel and not on the site.
        TvWebView.ClearHighlight();

        if (_first is not null)
        {
            _first.Focusable = true;
            _first.RequestFocus();
        }

        Arm();
    }

    // PlayerControl calls this only for a TV episode that has reached 90%.
    // The first arrival opens the panel so Next is visible without a secret
    // key combination; later visits can reopen it with Left.
    public static void SetNextAvailable(bool available)
    {
        var becameAvailable = available && _next?.Visibility != ViewStates.Visible;
        if (_next is not null)
            _next.Visibility = available ? ViewStates.Visible : ViewStates.Gone;
        if (becameAvailable && TvWebView.OnMoviePage)
            Show();
    }

    public static void Hide()
    {
        if (_root is null)
            return;

        _root.Visibility = ViewStates.Gone;
        TvWebView.Focus(TvWebView.Main);
    }

    // Every press inside the panel keeps it up a little longer.
    internal static void Arm()
    {
        var gen = ++_generation;
        _ = Task.Run(async () =>
        {
            await Task.Delay(9000);
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (gen == _generation && IsOpen)
                    Hide();
            });
        });
    }

    private static void Build(Activity activity)
    {
        var bar = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.Center);
        bar.SetPadding(Dp(16), Dp(10), Dp(16), Dp(10));
        bar.Focusable = true;
        bar.FocusableInTouchMode = true;

        // The bar is white at 30% opacity (4D out of FF), so it rests lightly
        // over the movie instead of hiding it. The dark buttons on top keep
        // their contrast and stay easy to read on a light bar.
        var bg = new Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(AndroidColor.ParseColor("#4DFFFFFF"));
        bg.SetCornerRadius(Dp(20));
        bg.SetStroke(Dp(1), AndroidColor.ParseColor("#80FFFFFF"));
        bar.Background = bg;

        var home = Key(activity, "Home");
        var beginning = Key(activity, "Beginning");
        var backward = Key(activity, "<< Back 10s");
        var playPause = Key(activity, "Play/Pause");
        var forward = Key(activity, "Forward 10s >>");
        var next = Key(activity, "Next");
        next.Visibility = ViewStates.Gone;
        _next = next;
        _beginning = beginning;
        _forward = forward;
        _backward = backward;

        home.Click += (_, _) =>
        {
            Hide();
            MainActivity.Current?.PanelBack();
        };

        beginning.Click += (_, _) =>
        {
            TvWebView.PlayerCommand("beginning");
            Arm();
        };

        // One button for both: it plays a paused movie and pauses a running
        // one, so the bar keeps a single clear spot in the middle.
        playPause.Click += (_, _) =>
        {
            TvWebView.PlayerCommand("toggle");
            Arm();
        };

        backward.Click += (_, _) =>
        {
            TvWebView.PlayerCommand("backward");
            Arm();
        };

        forward.Click += (_, _) =>
        {
            TvWebView.PlayerCommand("forward");
            Arm();
        };

        next.Click += (_, _) =>
        {
            SetNextAvailable(false);
            Hide();
            TvWebView.NextEpisode();
        };

        bar.AddView(home);
        bar.AddView(beginning);
        bar.AddView(backward);
        bar.AddView(playPause);
        bar.AddView(forward);
        bar.AddView(next);
        _first = home;

        var root = new FrameLayout(activity);
        var lp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent);
        lp.Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal;
        lp.BottomMargin = Dp(40);
        root.AddView(bar, lp);
        root.Visibility = ViewStates.Gone;
        _root = root;
    }

    private static Android.Widget.Button Key(Activity activity, string label)
    {
        var b = new Android.Widget.Button(activity);
        b.Text = label;
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        b.Focusable = true;
        b.FocusableInTouchMode = true;
        // Each button is only as wide as its words (with a small minimum), so the
        // bar stays narrow enough to show on screen, even with Next showing.
        b.SetMinWidth(Dp(84));
        b.SetPadding(Dp(14), 0, Dp(14), 0);
        Style(b, false);
        b.FocusChange += (_, e) =>
        {
            Style(b, e.HasFocus);
            if (e.HasFocus)
                Arm();
        };

        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(46));
        lp.SetMargins(Dp(5), 0, Dp(5), 0);
        b.LayoutParameters = lp;
        return b;
    }

    private static void Style(TextView b, bool focused)
    {
        // The bar behind is white and see-through, so the buttons are dark and
        // slightly see-through too: they keep their contrast over a bright
        // movie and let the frosted bar show through, like glass.
        var bg = new Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(AndroidColor.ParseColor(focused ? "#D9143C5A" : "#CC0B1A2B"));
        bg.SetCornerRadius(Dp(14));
        bg.SetStroke(Dp(2), AndroidColor.ParseColor(focused ? "#2BE0FF" : "#661E7FA0"));
        b.Background = bg;
        b.SetTextColor(AndroidColor.ParseColor(focused ? "#EAFDFF" : "#B8E4F2"));
    }
}
