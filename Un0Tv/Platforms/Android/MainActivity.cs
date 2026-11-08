using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.OS;
using Android.Webkit;
using AndroidView = Android.Views.View;
using AndroidKeycode = Android.Views.Keycode;

namespace Un0Tv;

[Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          Exported = true,
          LaunchMode = LaunchMode.SingleTop,
          HardwareAccelerated = true,
          ScreenOrientation = ScreenOrientation.Landscape,
          ConfigurationChanges = ConfigChanges.ScreenSize
                               | ConfigChanges.Orientation
                               | ConfigChanges.UiMode
                               | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize
                               | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionMain },
              Categories = new[] { Intent.CategoryLauncher })]
[IntentFilter(new[] { Intent.ActionMain },
              Categories = new[] { Intent.CategoryLeanbackLauncher })]
public class MainActivity : MauiAppCompatActivity
{
    private const int VpnRequest = 4711;

    // The window's own view, measured on every key press to tell whether the
    // on-screen keyboard is really showing.
    private AndroidView? _decor;

    // The running activity, so the home screen can start the VPN filter.
    public static MainActivity? Current { get; private set; }

    // Starts the whole-TV DNS filter. Android shows its own "allow this VPN"
    // box the first time; the answer comes back in OnActivityResult.
    public void StartVpnFlow()
    {
        var consent = Android.Net.VpnService.Prepare(this);
        if (consent is null)
            StartDnsService();
        else
            StartActivityForResult(consent, VpnRequest);
    }

    public void StopVpnFlow()
        => StopService(new Intent(this, typeof(DnsFilterService)));

    private void StartDnsService()
        => StartService(new Intent(this, typeof(DnsFilterService)));

    protected override void OnActivityResult(int requestCode, Android.App.Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == VpnRequest && resultCode == Android.App.Result.Ok)
            StartDnsService();

        MainPage.Instance?.RefreshVpn();
    }

    // Find the screen: first the direct handle, then by walking the Shell.
    private static MainPage? Page
    {
        get
        {
            if (MainPage.Instance is not null)
                return MainPage.Instance;

            var root = Microsoft.Maui.Controls.Application.Current?
                           .Windows.FirstOrDefault()?.Page;

            if (root is Shell shell)
                return shell.CurrentPage as MainPage;

            return root as MainPage;
        }
    }

    // The app catches the remote's arrows again and moves a highlight on the
    // page, because this site was built for phones and has no TV navigation of
    // its own. Back is still handled here too, so leaving a fullscreen movie or
    // closing a sign-in window works.
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        // The app's own letter pad is up: it owns the keys, because its buttons
        // are the only thing on screen that can be pressed. Back puts it away.
        if (KeyPad.IsOpen && e is not null)
        {
            if (e.Action == KeyEventActions.Down && e.KeyCode == AndroidKeycode.Back)
            {
                KeyPad.Hide();
                TvWebView.HideKeyboard();
                return true;
            }

            return base.DispatchKeyEvent(e);
        }

        // The command panel over a movie is up: the arrows and OK belong to its
        // own buttons, and Back closes it before the movie is disturbed.
        if (MediaPanel.IsOpen && e is not null)
        {
            if (e.Action == KeyEventActions.Down && e.KeyCode == AndroidKeycode.Back)
            {
                MediaPanel.Hide();
                return true;
            }

            MediaPanel.Arm();
            return base.DispatchKeyEvent(e);
        }

        // While the system keyboard is up, the keys belong to the box being
        // typed into: the caret already sits in it on the page, so every key is
        // handed back to the browser the way a normal keyboard would. Back is
        // kept, so it can put the keys away.
        //
        // Measured fresh on every press, never remembered: a remembered answer
        // could stay stuck at "keys are up" when the keyboard never actually
        // opened, and every arrow press was then swallowed - the remote looked
        // dead.
        if (KeysOnScreen() && e is not null && e.KeyCode != AndroidKeycode.Back)
        {
            // The keys take the letters, but the arrows still have to move the
            // highlight underneath: the browser's own focus movement does
            // nothing on this site, so without this the remote would look dead
            // for as long as the keys are up.
            if (e.Action == KeyEventActions.Down)
                NudgeFromKey(e.KeyCode);

            return base.DispatchKeyEvent(e);
        }

        // The startup check is on screen and the site has not loaded yet, so
        // there is no page to drive. The arrows move between the check's own
        // buttons; every other key is left to Android, which presses whichever
        // button the ring is on.
        if (Page?.IsGateVisible == true)
        {
            if (e is not null && e.Action == KeyEventActions.Down)
            {
                switch (e.KeyCode)
                {
                    case AndroidKeycode.DpadUp:
                    case AndroidKeycode.DpadDown:
                    case AndroidKeycode.DpadLeft:
                    case AndroidKeycode.DpadRight:
                        var gx = e.KeyCode == AndroidKeycode.DpadLeft ? -1
                               : e.KeyCode == AndroidKeycode.DpadRight ? 1 : 0;
                        var gy = e.KeyCode == AndroidKeycode.DpadUp ? -1
                               : e.KeyCode == AndroidKeycode.DpadDown ? 1 : 0;
                        Page.MoveGateFocus(gx, gy);
                        return true;
                }
            }

            return base.DispatchKeyEvent(e);
        }

        // The remote's media keys drive the movie player: play, pause, the
        // play/pause toggle, forward 10 seconds and back 10 seconds. They are
        // always taken, because a frame with no movie simply ignores the
        // command - and a viewer pressing Pause must never be ignored.
        if (e is not null && e.Action == KeyEventActions.Down)
        {
            var playerAction = e.KeyCode switch
            {
                AndroidKeycode.MediaPlay => "play",
                AndroidKeycode.MediaPause => "pause",
                AndroidKeycode.MediaPlayPause => "toggle",
                AndroidKeycode.MediaFastForward => "forward",
                AndroidKeycode.MediaRewind => "back",
                _ => null,
            };

            if (playerAction is not null)
            {
                TvWebView.PlayerCommand(playerAction);
                return true;
            }
        }

        // A movie on screen: the Left key opens the app's own command panel,
        // because the player's controls sit inside a player frame the app can
        // never focus - which is why the remote's buttons did nothing there.
        //
        // Checked before the fullscreen branch on purpose: the panel is exactly
        // what a viewer needs while the picture fills the screen. Fullscreen
        // counts as "a movie is on", so the panel still opens even if the
        // player's own report never arrived.
        if (e is not null && e.Action == KeyEventActions.Down
            && e.KeyCode == AndroidKeycode.DpadLeft
            && (TvWebView.OnMoviePage || TvWebView.IsFullscreen))
        {
            MediaPanel.Show();
            return true;
        }

        // A movie filling the screen: Back leaves fullscreen, and every other
        // key belongs to the player's own controls.
        if (TvWebView.IsFullscreen && e is not null
            && e.KeyCode != AndroidKeycode.Back)
            return base.DispatchKeyEvent(e);

        if (e is not null && e.Action == KeyEventActions.Down)
        {
            var handled = true;

            switch (e.KeyCode)
            {
                case AndroidKeycode.DpadUp:
                case AndroidKeycode.DpadDown:
                case AndroidKeycode.DpadLeft:
                case AndroidKeycode.DpadRight:
                    NudgeFromKey(e.KeyCode);
                    break;

                case AndroidKeycode.DpadCenter:
                case AndroidKeycode.Enter:
                case AndroidKeycode.NumpadEnter:
                    Page?.ActivateFocus();
                    break;

                case AndroidKeycode.Back:
                    // Keys covering the page: Back puts them away first, which is
                    // what a viewer expects when they are done searching.
                    if (KeysOnScreen())
                    {
                        TvWebView.HideKeyboard();
                        break;
                    }

                    // A movie in fullscreen: Back leaves fullscreen first.
                    if (TvWebView.IsFullscreen)
                    {
                        TvWebView.ExitFullscreen();
                        break;
                    }

                    // A sign-in window is open on top: Back closes it and
                    // returns to the page underneath.
                    if (TvWebView.IsPopupOpen)
                    {
                        TvWebView.ClosePopup();
                        break;
                    }

                    if (Page?.HandleBack() == true)
                        break; // consumed, go back inside the site

                    handled = false;
                    break;

                default:
                    handled = false;
                    break;
            }

            if (handled)
                return true;
        }

        return base.DispatchKeyEvent(e);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Window?.SetFlags(
            WindowManagerFlags.Fullscreen | WindowManagerFlags.KeepScreenOn,
            WindowManagerFlags.Fullscreen | WindowManagerFlags.KeepScreenOn);

        base.OnCreate(savedInstanceState);
        Current = this;

        // The on-screen keyboard needs the window to resize, so a key press can
        // tell it apart from the page by measuring the window.
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        _decor = Window?.DecorView;
    }

    // The keys are up only while the window really is shorter than the screen.
    private bool KeysOnScreen() => _decor is not null && KeyboardIsUp(_decor);

    // The same answer, for the web view: after asking Android for its keyboard
    // it looks here to see whether any keys actually appeared.
    public static bool SoftKeysUp()
        => Current?._decor is not null && KeyboardIsUp(Current._decor);

    // The command panel's Back button: leave the fullscreen picture first, then
    // a sign-in window, then go to the dashboard URL.
    internal void PanelBack()
    {
        if (TvWebView.IsFullscreen)
        {
            TvWebView.ExitFullscreen();
            return;
        }

        if (TvWebView.IsPopupOpen)
        {
            TvWebView.ClosePopup();
            return;
        }

        // Go to the dashboard URL directly.
        var page = Page;
        if (page is not null)
        {
            page.LoadDashboard();
            return;
        }

        // Fallback: step back inside the site.
        TvWebView.GoBackPage();
    }

    // Moves the page's highlight for an arrow key, wherever the key is caught.
    private static void NudgeFromKey(AndroidKeycode key)
    {
        var dx = key == AndroidKeycode.DpadLeft ? -1
               : key == AndroidKeycode.DpadRight ? 1 : 0;
        var dy = key == AndroidKeycode.DpadUp ? -1
               : key == AndroidKeycode.DpadDown ? 1 : 0;

        if (dx == 0 && dy == 0)
            return;

        Page?.MoveFocus(dx, dy);
    }

    private static bool KeyboardIsUp(AndroidView decor)
    {
        try
        {
            var visible = new Android.Graphics.Rect();
            decor.GetWindowVisibleDisplayFrame(visible);
            var hidden = decor.Height - visible.Bottom;
            return hidden > decor.Height * 0.15;
        }
        catch
        {
            return false;
        }
    }
}
