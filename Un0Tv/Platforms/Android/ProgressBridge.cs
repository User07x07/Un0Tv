using Android.Webkit;
using Java.Interop;

namespace Un0Tv;

// The page script hands its watch progress to the app through this object, as
// window.Un0TvNative.saveProgress(text). It only stores the text in
// WatchProgress; it runs no commands.
//
// It also tells the app whether a movie is playing, so Left on the remote can
// open the player panel at the right time.
internal sealed class ProgressBridge : Java.Lang.Object
{
    [JavascriptInterface]
    [Export("saveProgress")]
    public void SaveProgress(string json) => WatchProgress.Save(json);

    [JavascriptInterface]
    [Export("mediaPlaying")]
    public void MediaPlaying(int playing)
    {
        try { Overlays.MediaActive = playing != 0; } catch { }
    }
}
