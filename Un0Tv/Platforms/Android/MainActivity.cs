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

        // A movie filling the screen: its own player controls own the arrows.
        // Back still has to leave fullscreen, so that one key is handled below.
        if (TvWebView.IsFullscreen && e is not null && e.KeyCode != AndroidKeycode.Back)
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
