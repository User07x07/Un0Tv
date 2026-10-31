using Android.App;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidButton = Android.Widget.Button;
using AndroidColor = Android.Graphics.Color;
using AndroidKeycode = Android.Views.Keycode;
using Orientation = Android.Widget.Orientation;

namespace Un0Tv;

// The two things that sit over the site: the movie control panel, and the
// on-screen keyboard for the site's search box.
//
// A TV has no touch screen, so neither can come from the page itself. Both are
// drawn by the app as plain Android views above the browser:
//
//   Player panel - opened with Left on the remote while a movie is playing, or
//                  while a movie fills the screen. Pause, Play, 10 seconds back,
//                  10 seconds forward, and Close. It hides by itself after a
//                  few seconds, and Left closes it again.
//
//   Keyboard     - opens when the remote lands on the site's search box. The
//                  letters are typed into that box by the page script
//                  (FieldInput), through the same events a person's typing
//                  produces, so the site's search reacts.
//
// Every call is made on the screen's own thread, and a missing screen or a
// closed browser simply means "nothing to draw yet" - never an exception.
internal static class Overlays
{
    private static Activity? Act => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

    public static bool PlayerOpen { get; private set; }
    public static bool KeyboardOpen { get; private set; }

    // Set from the page script: true while a movie is actually playing. The
    // remote's Left key opens the panel only then, so moving around the site
    // with Left keeps working as before.
    public static bool MediaActive { get; set; }

    // True once the viewer closed the keyboard on this page, so landing on the
    // search box again does not reopen it straight away.
    private static bool _suppressed;

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

    // ---------------------------------------------------------------
    // The on-screen keyboard
    // ---------------------------------------------------------------

    private static FrameLayout? _keyHost;
    private static TextView? _preview;
    private static readonly List<List<AndroidButton>> _rows = new();
    private static int _row;
    private static int _col;
    private static string _typed = "";

    public static void ShowKeyboard()
    {
        var act = Act;
        if (act is null)
            return;

        act.RunOnUiThread(() =>
        {
            try
            {
                BuildKeyboard(act);
                if (_keyHost is null)
                    return;

                _keyHost.Visibility = ViewStates.Visible;
                KeyboardOpen = true;
                _row = 0;
                _col = 0;
                Highlight();
            }
            catch { }
        });
    }

    public static void HideKeyboard(bool byViewer)
    {
        KeyboardOpen = false;
        if (byViewer)
            _suppressed = true;

        _keyHost?.Visibility = ViewStates.Gone;

        try { MainPage.Instance?.RunInPage("window.__un0tvField&&window.__un0tvField.blur();"); } catch { }
        TvWebView.Focus(TvWebView.Popup ?? TvWebView.Main);
    }

    // Called by the page when the remote lands on a box the viewer can type in.
    public static void NoteFieldFocused()
    {
        var act = Act;
        if (act is null)
            return;

        act.RunOnUiThread(() =>
        {
            if (KeyboardOpen || PlayerOpen || _suppressed)
                return;

            // Never over one of the app's own screens.
            if (MainPage.Instance?.IsGateVisible == true)
                return;

            ShowKeyboard();
        });
    }

    // A new page means the viewer's earlier "hide it" no longer applies.
    public static void ResetForPage()
    {
        _suppressed = false;
        _typed = "";
        MediaActive = false;
        if (KeyboardOpen)
            HideKeyboard(false);
        if (PlayerOpen)
            ClosePlayer();
    }

    private static void BuildKeyboard(Activity act)
    {
        if (_keyHost is not null && _keyHost.IsAttachedToWindow)
            return;

        var host = new FrameLayout(act);

        var card = new LinearLayout(act) { Orientation = Orientation.Vertical };
        card.SetBackgroundColor(C("#F0070B16"));
        card.SetPadding(Dp(28), Dp(14), Dp(28), Dp(20));

        _preview = new TextView(act)
        {
            Text = "Search",
            Ellipsize = Android.Text.TextUtils.TruncateAt.Start
        };
        _preview.SetTextColor(Cs("#6C6C84"));
        _preview.SetTextSize(Android.Util.ComplexUnitType.Sp, 18);
        _preview.SetPadding(Dp(4), 0, Dp(4), Dp(12));
        card.AddView(_preview, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(44)));

        _rows.Clear();

        AddLetters(card, "1234567890");
        AddLetters(card, "QWERTYUIOP");
        AddLetters(card, "ASDFGHJKL");
        AddLetters(card, "ZXCVBNM");

        AddKeyRow(card, new List<(string Label, string Action)>
        {
            ("Space", "SPACE"),
            ("Delete", "DEL"),
            ("Search", "ENTER"),
            ("Hide", "HIDE")
        });

        var hint = new TextView(act) { Text = "Arrows move, OK types, Back hides the keys." };
        hint.SetTextColor(Cs("#6C6C84"));
        hint.SetTextSize(Android.Util.ComplexUnitType.Sp, 12);
        hint.SetPadding(Dp(4), Dp(8), 0, 0);
        card.AddView(hint);

        var bottom = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        bottom.Gravity = GravityFlags.Bottom;

        host.AddView(card, bottom);
        host.Visibility = ViewStates.Gone;

        if (act.Window?.DecorView is ViewGroup decor)
            decor.AddView(host, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        _keyHost = host;
    }

    private static void AddLetters(LinearLayout card, string keys)
    {
        var list = new List<(string Label, string Action)>();
        foreach (var k in keys)
            list.Add((k.ToString(), k.ToString()));
        AddKeyRow(card, list);
    }

    private static void AddKeyRow(LinearLayout card, List<(string Label, string Action)> keys)
    {
        var row = new LinearLayout(card.Context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterHorizontal);

        var buttons = new List<AndroidButton>();
        foreach (var (label, action) in keys)
        {
            var button = new AndroidButton(row.Context!)
            {
                Text = label,
                Focusable = true,
                FocusableInTouchMode = true,
                Tag = action
            };
            button.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
            button.SetPadding(0, 0, 0, 0);
            Paint(button, "#E6F1FF", "#162036");

            var wide = action is "SPACE" or "DEL" or "ENTER" or "HIDE";
            var layout = new LinearLayout.LayoutParams(wide ? Dp(120) : Dp(72), Dp(60));
            layout.RightMargin = Dp(8);
            layout.BottomMargin = Dp(8);
            row.AddView(button, layout);

            button.Click += (_, _) => Press(action);
            buttons.Add(button);
        }

        card.AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        _rows.Add(buttons);
    }

    private static void Highlight()
    {
        for (var r = 0; r < _rows.Count; r++)
        {
            for (var c = 0; c < _rows[r].Count; c++)
            {
                var on = r == _row && c == _col;
                if (on) Paint(_rows[r][c], "#FFFFFF", "#0E7490");
                else Paint(_rows[r][c], "#E6F1FF", "#162036");
            }
        }
    }

    private static void MoveKey(int dx, int dy)
    {
        if (_rows.Count == 0)
            return;

        if (dy != 0)
        {
            _row = (_row + dy + _rows.Count) % _rows.Count;
            if (_col >= _rows[_row].Count)
                _col = _rows[_row].Count - 1;
        }
        else if (dx != 0)
        {
            var count = _rows[_row].Count;
            _col = (_col + dx + count) % count;
        }

        Highlight();
    }

    private static void Press(string action)
    {
        switch (action)
        {
            case "SPACE":
                Insert(" ");
                break;

            case "DEL":
                if (_typed.Length > 0)
                    _typed = _typed.Substring(0, _typed.Length - 1);
                MainPage.Instance?.RunInPage("window.__un0tvField&&window.__un0tvField.backspace();");
                ShowTyped();
                break;

            case "ENTER":
                MainPage.Instance?.RunInPage("window.__un0tvField&&window.__un0tvField.submit();");
                HideKeyboard(false);
                break;

            case "HIDE":
                HideKeyboard(true);
                break;

            default:
                Insert(action);
                break;
        }
    }

    private static void Insert(string text)
    {
        var safe = text.Replace("\\", "\\\\").Replace("'", "\\'");
        MainPage.Instance?.RunInPage(
            "window.__un0tvField&&window.__un0tvField.insert('" + safe + "');");
        _typed += text;
        ShowTyped();
    }

    private static void ShowTyped()
    {
        if (_preview is null)
            return;

        _preview.Text = _typed.Length == 0 ? "Search" : _typed;
        try { _preview.SetTextColor(Cs(_typed.Length == 0 ? "#6C6C84" : "#E6F1FF")); } catch { }
    }

    // Every remote key while the keyboard is up. Returns true when the key was
    // used, so nothing reaches the page behind it.
    public static bool HandleKeyboardKey(AndroidKeycode code)
    {
        switch (code)
        {
            case AndroidKeycode.DpadUp:
                MoveKey(0, -1);
                return true;

            case AndroidKeycode.DpadDown:
                MoveKey(0, 1);
                return true;

            case AndroidKeycode.DpadLeft:
                MoveKey(-1, 0);
                return true;

            case AndroidKeycode.DpadRight:
                MoveKey(1, 0);
                return true;

            case AndroidKeycode.DpadCenter:
            case AndroidKeycode.Enter:
            case AndroidKeycode.NumpadEnter:
                if (_row >= 0 && _row < _rows.Count && _col < _rows[_row].Count)
                {
                    var action = _rows[_row][_col].Tag?.ToString();
                    if (!string.IsNullOrEmpty(action))
                        Press(action);
                }
                return true;

            case AndroidKeycode.Back:
                HideKeyboard(true);
                return true;

            default:
                return true;
        }
    }
}
