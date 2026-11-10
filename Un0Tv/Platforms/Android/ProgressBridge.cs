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

    // Called by the player's own frame whenever the movie starts, stops or
    // goes away. The Left key opens the command panel only while a movie is
    // on screen, so it keeps moving the highlight everywhere else.
    [JavascriptInterface]
    [Export("mediaState")]
    public void MediaState(string on)
        => TvWebView.SetMoviePlaying(on == "1");

    // Called when the box goes away or the keys are no longer wanted, so the
    // keyboard closes with it.
    [JavascriptInterface]
    [Export("closeKeyboard")]
    public void CloseKeyboard()
    {
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            TvWebView.HideKeyboard();
            KeyPad.Hide();
        });
    }

    // Only trusted player events can request the episode-only Next button.
    [JavascriptInterface]
    [Export("nextEpisode")]
    public void NextEpisode(string available)
        => TvWebView.SetNextAvailable(available == "1");

    // Called by PageRoute on every navigation (pushState, popstate, hashchange,
    // initial load). The app keeps this to decide whether the Left key should
    // open the movie command panel.
    [JavascriptInterface]
    [Export("route")]
    public void SetRoute(string path)
        => Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            TvWebView.SetRoute(path));
}