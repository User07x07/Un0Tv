using Android.App;
using Android.Views;
using Android.Widget;
using AndroidColor = Android.Graphics.Color;

namespace Un0Tv;

// The small command panel the remote's Left key opens over a movie: Back,
// Play and Pause.
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

        var bg = new Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(AndroidColor.ParseColor("#EE07121F"));
        bg.SetCornerRadius(Dp(20));
        bg.SetStroke(Dp(2), AndroidColor.ParseColor("#2BE0FF"));
        bar.Background = bg;

        var back = Key(activity, "Back", 150);
        var play = Key(activity, "Play", 150);
        var pause = Key(activity, "Pause", 150);

        back.Click += (_, _) =>
        {
            Hide();
            MainActivity.Current?.PanelBack();
        };

        play.Click += (_, _) =>
        {
            TvWebView.PlayerCommand("play");
            Arm();
        };

        pause.Click += (_, _) =>
        {
            TvWebView.PlayerCommand("pause");
            Arm();
        };

        bar.AddView(back);
        bar.AddView(play);
        bar.AddView(pause);
        _first = back;

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

    private static Android.Widget.Button Key(Activity activity, string label, int widthDp)
    {
        var b = new Android.Widget.Button(activity);
        b.Text = label;
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        b.Focusable = true;
        b.FocusableInTouchMode = true;
        b.SetPadding(Dp(10), 0, Dp(10), 0);
        Style(b, false);
        b.FocusChange += (_, e) =>
        {
            Style(b, e.HasFocus);
            if (e.HasFocus)
                Arm();
        };

        var lp = new LinearLayout.LayoutParams(Dp(widthDp), Dp(46));
        lp.SetMargins(Dp(5), 0, Dp(5), 0);
        b.LayoutParameters = lp;
        return b;
    }

    private static void Style(TextView b, bool focused)
    {
        var bg = new Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(AndroidColor.ParseColor(focused ? "#123C5A" : "#0B1A2B"));
        bg.SetCornerRadius(Dp(14));
        bg.SetStroke(Dp(2), AndroidColor.ParseColor(focused ? "#2BE0FF" : "#1E7FA0"));
        b.Background = bg;
        b.SetTextColor(AndroidColor.ParseColor(focused ? "#EAFDFF" : "#9FD8EA"));
    }
}
