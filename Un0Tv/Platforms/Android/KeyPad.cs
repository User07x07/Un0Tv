using Android.App;
using Android.Views;
using Android.Widget;
using AndroidColor = Android.Graphics.Color;

namespace Un0Tv;

// The app's own letter pad, used when the TV has no system keyboard.
//
// Android only shows its own keyboard after a real touch, and many TVs have
// no keyboard at all - so asking for it often shows nothing. This pad is made
// of real Android buttons, so the D-pad always reaches it, and every press
// types into the search box through the page's own typing script.
//
// It sits at the bottom of the screen and never covers the box being typed in.
internal static class KeyPad
{
    private static readonly string[] Rows =
    {
        "1234567890",
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
    };

    private static FrameLayout? _root;
    private static Android.Widget.Button? _first;

    public static bool IsOpen => _root is not null && _root.Visibility == ViewStates.Visible;

    private static Activity? Act => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

    private static int Dp(float v) => (int)(v * (Act?.Resources?.DisplayMetrics?.Density ?? 1f) + 0.5f);

    public static void Show()
    {
        var activity = Act;
        if (activity?.Window?.DecorView is not ViewGroup decor)
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

        // The page's highlight would sit under the pad and look like a fault.
        TvWebView.ClearHighlight();

        if (_first is not null)
            _first.RequestFocus();
    }

    public static void Hide()
    {
        if (_root is null)
            return;

        _root.Visibility = ViewStates.Gone;
        TvWebView.Focus(TvWebView.Main);
    }

    private static void Build(Activity activity)
    {
        var pad = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        pad.SetGravity(GravityFlags.CenterHorizontal);
        pad.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        pad.Focusable = true;
        pad.FocusableInTouchMode = true;

        var bg = new Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(AndroidColor.ParseColor("#F00A1626"));
        bg.SetCornerRadius(Dp(16));
        bg.SetStroke(Dp(2), AndroidColor.ParseColor("#2BE0FF"));
        pad.Background = bg;

        for (var r = 0; r < Rows.Length; r++)
        {
            var row = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterHorizontal);

            for (var i = 0; i < Rows[r].Length; i++)
            {
                var ch = Rows[r][i].ToString();
                var key = Key(activity, ch, 52);
                key.Click += (_, _) => Type(ch);
                row.AddView(key);
                if (_first is null && r == 0 && i == 0)
                    _first = key;
            }

            pad.AddView(row);
        }

        // The bottom row: a space, erase, search, and close.
        var tools = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        tools.SetGravity(GravityFlags.CenterHorizontal);

        var space = Key(activity, "space", 180);
        space.Click += (_, _) => Type(" ");

        var erase = Key(activity, "erase", 130);
        erase.Click += (_, _) => Erase();

        var search = Key(activity, "search", 130);
        search.Click += (_, _) =>
        {
            TvWebView.SubmitField();
            Hide();
            TvWebView.HideKeyboard();
        };

        var close = Key(activity, "close", 110);
        close.Click += (_, _) =>
        {
            Hide();
            TvWebView.HideKeyboard();
        };

        tools.AddView(space);
        tools.AddView(erase);
        tools.AddView(search);
        tools.AddView(close);
        pad.AddView(tools);

        var root = new FrameLayout(activity);
        var lp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent);
        lp.Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal;
        lp.BottomMargin = Dp(10);
        root.AddView(pad, lp);
        root.Visibility = ViewStates.Gone;
        _root = root;
    }

    private static void Type(string text)
    {
        TvWebView.TypeText(text);
    }

    // Backspace does not auto-hide the pad.
    private static void Erase()
    {
        TvWebView.EraseText();
    }

    private static Android.Widget.Button Key(Activity activity, string label, int widthDp)
    {
        var b = new Android.Widget.Button(activity);
        b.Text = label;
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        b.Focusable = true;
        b.FocusableInTouchMode = true;
        b.SetPadding(Dp(4), 0, Dp(4), 0);
        Style(b, false);
        b.FocusChange += (_, e) => Style(b, e.HasFocus);

        var lp = new LinearLayout.LayoutParams(Dp(widthDp), Dp(40));
        lp.SetMargins(Dp(3), Dp(2), Dp(3), Dp(2));
        b.LayoutParameters = lp;
        return b;
    }

    private static void Style(TextView b, bool focused)
    {
        var bg = new Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(AndroidColor.ParseColor(focused ? "#123C5A" : "#0B1A2B"));
        bg.SetCornerRadius(Dp(8));
        bg.SetStroke(Dp(2), AndroidColor.ParseColor(focused ? "#2BE0FF" : "#1E7FA0"));
        b.Background = bg;
        b.SetTextColor(AndroidColor.ParseColor(focused ? "#EAFDFF" : "#9FD8EA"));
    }
}
