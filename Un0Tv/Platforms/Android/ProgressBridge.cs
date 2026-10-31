using Android.Webkit;
using Java.Interop;

namespace Un0Tv;

// The page script hands its watch progress to the app through this object, as
// window.Un0TvNative.saveProgress(text). It only stores the text in
// WatchProgress; it runs no commands.
internal sealed class ProgressBridge : Java.Lang.Object
{
    [JavascriptInterface]
    [Export("saveProgress")]
    public void SaveProgress(string json) => WatchProgress.Save(json);
}
