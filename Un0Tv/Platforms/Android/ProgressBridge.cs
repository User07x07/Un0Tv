using Android.Webkit;
using Java.Interop;

namespace Un0Tv;

// The page script talks to the app through this object, as
// window.Un0TvNative.<name>(...). It stores watch progress and raises the TV's
// own keyboard for the site's search box; it runs no other commands.
internal sealed class ProgressBridge : Java.Lang.Object
{
    // Called when a movie's watch time changes. The text is kept in
    // WatchProgress and nothing else happens with it.
    [JavascriptInterface]
    [Export("saveProgress")]
    public void SaveProgress(string json) => WatchProgress.Save(json);

    // Called when the viewer presses OK on a box they can type in. A TV has no
    // touch screen, so the box on its own can never raise the keyboard: the app
    // has to ask for it. The call arrives on a page thread, so it is handed to
    // the screen thread, where views may be touched.
    [JavascriptInterface]
    [Export("openKeyboard")]
    public void OpenKeyboard()
        => Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(
            TvWebView.ShowKeyboard);

    // Called when the box goes away or the keys are no longer wanted, so the
    // keyboard closes with it.
    [JavascriptInterface]
    [Export("closeKeyboard")]
    public void CloseKeyboard()
        => Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(
            TvWebView.HideKeyboard);
}
