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
        // While the sign-in window is up and the on-screen keyboard is showing,
        // every key belongs to the keyboard.
        if (TvWebView.IsPopupOpen && TvWebView.KeyboardVisible)
            return base.DispatchKeyEvent(e);

        // The app's own keyboard is up: every key types, moves on the letters or
        // hides it again. Nothing reaches the page behind it.
        if (e is not null && e.Action == KeyEventActions.Down && Overlays.KeyboardOpen)
            return Overlays.HandleKeyboardKey(e.KeyCode);

        // The movie control panel is up: the arrows move on it, OK presses, and
        // Left closes it again - the same key that opened it.
        if (e is not null && e.Action == KeyEventActions.Down && Overlays.PlayerOpen)
        {
            switch (e.KeyCode)
            {
                case AndroidKeycode.DpadLeft:
                    Overlays.ClosePlayer();
                    return true;

                case AndroidKeycode.Back:
                    Overlays.ClosePlayer();
                    return true;

                case AndroidKeycode.DpadUp:
                case AndroidKeycode.DpadDown:
                case AndroidKeycode.DpadCenter:
                case AndroidKeycode.Enter:
                case AndroidKeycode.NumpadEnter:
                    // Left to Android: the panel is made of real buttons, so the
                    // system moves the ring and presses the right one.
                    Overlays.TouchPlayer();
                    return base.DispatchKeyEvent(e);

                default:
                    Overlays.TouchPlayer();
                    return base.DispatchKeyEvent(e);
            }
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
        // play/pause toggle, forward 10 seconds and back 10 seconds.
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

        // A movie filling the screen: Left opens the player panel, and Back
        // leaves fullscreen. Every other key belongs to the player's own
        // controls.
        if (TvWebView.IsFullscreen && e is not null
            && e.KeyCode != AndroidKeycode.Back
            && e.KeyCode != AndroidKeycode.DpadLeft)
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
                    // Left while a movie is on, or while it fills the screen, is
                    // the way to the player panel. Every other arrow keeps
                    // moving the highlight on the page.
                    if (e.KeyCode == AndroidKeycode.DpadLeft
                        && (TvWebView.IsFullscreen || Overlays.MediaActive))
                    {
                        Overlays.OpenPlayer();
                        break;
                    }

                    var dx = e.KeyCode == AndroidKeycode.DpadLeft ? -1
                           : e.KeyCode == AndroidKeycode.DpadRight ? 1 : 0;
                    var dy = e.KeyCode == AndroidKeycode.DpadUp ? -1
                           : e.KeyCode == AndroidKeycode.DpadDown ? 1 : 0;
                    Page?.MoveFocus(dx, dy);
                    break;

                case AndroidKeycode.DpadCenter:
                case AndroidKeycode.Enter:
                case AndroidKeycode.NumpadEnter:
                    Page?.ActivateFocus();
                    break;

                case AndroidKeycode.Back:
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

        // The on-screen keyboard needs the window to resize, so the layout
        // below can tell when it is up and hand the keys over to it.
        Window?.SetSoftInputMode(SoftInput.AdjustResize);

        if (Window?.DecorView is AndroidView decor)
        {
            var observer = decor.ViewTreeObserver;
            if (observer is not null)
                observer.GlobalLayout += (_, _) => TvWebView.KeyboardVisible = KeyboardIsUp(decor);
        }
    }

    private static bool KeyboardIsUp(AndroidView decor)
    {
        var visible = new Android.Graphics.Rect();
        decor.GetWindowVisibleDisplayFrame(visible);
        var hidden = decor.Height - visible.Bottom;
        return hidden > decor.Height * 0.15;
    }
}
