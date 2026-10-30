using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Widget;
using AndroidUri = Android.Net.Uri;

namespace Un0Tv;

// Before the site opens, Un0Tv wants the TV's own ad filter running. That app
// is "DNS Ad Blocker & App Firewall" by LL Cloud Apps (its short name is
// Repel), package app.llcloud.repel.
//
// Android does not allow one app to change another app's settings, so this
// class cannot switch Repel's filter on itself. What it CAN do is check
// whether the filter is installed and allowed to run, open the right screens
// for you, and refuse to carry on quietly until both are true - which is what
// the startup gate in MainPage does.
internal static class BlockerGate
{
    // The filter app on Google Play.
    public const string PackageId = "app.llcloud.repel";

    private const string PlayWeb =
        "https://play.google.com/store/apps/details?id=" + PackageId + "&hl=en";

    private static Context Ctx => Android.App.Application.Context;

    // Is the filter app on this TV at all?
    public static bool IsInstalled
    {
        get
        {
            try
            {
                Ctx.PackageManager?.GetPackageInfo(PackageId, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // Drawing over other apps is the permission the filter's own "Protected"
    // switch depends on. Android added it in version 6 (API 23), so anything
    // older is treated as already allowed.
    public static bool HasPermission
    {
        get
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.M)
                    return true;

                return Settings.CanDrawOverlays(Ctx);
            }
            catch
            {
                return true; // cannot tell - do not block the viewer over it
            }
        }
    }

    // Both steps done: the filter is there and allowed to work.
    public static bool Ready => IsInstalled && HasPermission;

    // Opens Google Play on the filter's page. The store app is tried first,
    // because the web address needs a browser, which many TVs do not have.
    public static void OpenPlayStore()
    {
        if (TryStart(new Intent(Intent.ActionView,
                AndroidUri.Parse("market://details?id=" + PackageId))))
            return;

        if (TryStart(new Intent(Intent.ActionView, AndroidUri.Parse(PlayWeb))))
            return;

        Say("Google Play was not found on this TV. Install the filter app, then come back.");
    }

    // Opens the filter's own screen, where its "Ad and tracker filter" switch
    // can be turned on by hand. If the app has no launch screen, the nearest
    // settings page is opened instead.
    public static void OpenApp()
    {
        try
        {
            var launch = Ctx.PackageManager?.GetLaunchIntentForPackage(PackageId);
            if (launch is not null)
            {
                launch.AddFlags(ActivityFlags.NewTask);
                Ctx.StartActivity(launch);
                return;
            }
        }
        catch { }

        // No launch screen (or it vanished): try the permission page, then the
        // app's info page, then the store.
        if (OpenOverlaySettings())
            return;

        if (TryStart(new Intent(Settings.ActionApplicationDetailsSettings,
                AndroidUri.Parse("package:" + PackageId))))
            return;

        OpenPlayStore();
    }

    // Opens the TV screen where this one app is allowed to draw over others.
    // Some TVs hide this page, hence the true/false answer.
    private static bool OpenOverlaySettings()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            return false;

        // The page for the filter app itself, when the TV offers one.
        if (TryStart(new Intent(Settings.ActionManageOverlayPermission,
                AndroidUri.Parse("package:" + PackageId))))
            return true;

        return TryStart(new Intent(Settings.ActionManageOverlayPermission));
    }

    private static bool TryStart(Intent intent)
    {
        try
        {
            intent.AddFlags(ActivityFlags.NewTask);
            Ctx.StartActivity(intent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Say(string message)
    {
        try
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(
                () => Toast.MakeText(Ctx, message, ToastLength.Long)?.Show());
        }
        catch { }
    }
}
