namespace Un0Tv;

// The movie players start their video muted, because a browser only lets a
// video play on its own while it is silent. That is why the picture appeared
// but no sound came out.
//
// This script runs at "document start" in EVERY frame - the site, and the
// player's own frame too - so it reaches the player's video element wherever
// it is. It takes the mute off and asks the video to play, and puts the sound
// back if the player mutes it again.
//
// The one thing it never does is fight the viewer: a video that was paused on
// purpose is not started again, because it only ever tries to start a video
// that has not been started by this script before.
internal static class AudioAutoplay
{
    public static readonly string Js = """
(function () {
  if (window.__un0tvAudio) return;
  window.__un0tvAudio = 1;

  var last = 0;

  function isMedia(el) {
    try {
      if (!el || !el.tagName) return false;
      var t = el.tagName.toLowerCase();
      return t === "video" || t === "audio";
    } catch (e) { return false; }
  }

  // Sound on: undo the mute the player applied for autoplay.
  function sound(el) {
    try {
      if (el.muted) el.muted = false;
      if (el.volume === 0) el.volume = 1;
    } catch (e) { }
  }

  // Start the video, once per element. A failed attempt (no data yet, or a
  // browser that still wants a click) clears the flag so it can try again.
  function start(el) {
    try {
      if (!el || el.__un0tvStart || el.ended) return;
      if (!el.paused) return;
      if (el.readyState < 1) return;
      el.__un0tvStart = 1;
      var p = el.play();
      var onFail = p && p["catch"];
      if (onFail) onFail.call(p, function () { el.__un0tvStart = 0; });
    } catch (e) { }
  }

  function run() {
    last = Date.now();
    try {
      var list = document.querySelectorAll("video, audio");
      for (var i = 0; i < list.length; i++) {
        sound(list[i]);
        start(list[i]);
      }
    } catch (e) { }
  }

  // The player mutes the video again the moment it starts, so react to that at
  // once instead of waiting for the next sweep.
  var EVENTS = ["volumechange", "play", "playing", "loadstart", "durationchange", "canplay"];
  for (var e = 0; e < EVENTS.length; e++) {
    try {
      document.addEventListener(EVENTS[e], function (ev) {
        var t = ev.target;
        if (!isMedia(t)) return;
        sound(t);
        if (ev.type !== "volumechange") start(t);
      }, true);
    } catch (e2) { }
  }

  // Keep watch, but only once every 900ms, so a busy player cannot be slowed.
  try {
    setInterval(function () {
      if (Date.now() - last > 900) run();
    }, 900);
  } catch (e) { }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", run, { once: true });
  } else {
    run();
  }
})();
""";
}
