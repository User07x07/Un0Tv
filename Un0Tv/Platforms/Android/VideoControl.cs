namespace Un0Tv;

// Direct control of the movie's own video element, inside EVERY frame.
//
// The player sits in a frame from another website and builds its video with a
// blob: address - an address that only exists inside that frame's memory. So
// the app never tries to fetch the movie (an Android player could not open a
// blob anyway); it talks to the video element the frame already feeds itself.
// That works whether the movie is a real address or a blob.
//
// Registered at document start in every frame, so it reaches the player's own
// frame wherever it is. Commands arrive from the page the viewer sees (see
// PlayerControl), and every frame passes the command on to the frames below
// it, so a player nested two frames deep still hears it.
//
// It also tells the page how the movie is doing, which is how the app knows
// the Left key should open its command panel.
internal static class VideoControl
{
    public static readonly string Js = """
(function () {
  if (window.__un0tvVideo) return;
  window.__un0tvVideo = 1;

  var HOP = '__un0tvHop';
  var lastSay = 0;

  // The movie is the biggest video on screen, so a small background video or
  // an advert never wins over it.
  function biggest() {
    var best = null, area = 0;
    try {
      var list = document.querySelectorAll('video');
      for (var i = 0; i < list.length; i++) {
        var v = list[i], a = 0;
        try { a = (v.clientWidth || 0) * (v.clientHeight || 0); } catch (e) { a = 0; }
        if (!best || a >= area) { best = v; area = a; }
      }
    } catch (e) { return null; }
    return best;
  }

  // null means "this frame has no movie", so it says nothing at all: a frame
  // without a movie must never tell the app the movie is gone.
  function movieOn() {
    var v = biggest();
    if (!v) return null;
    try {
      if (!v.paused) return true;
      if (v.currentTime > 0.5) return true;
      if (isFinite(v.duration) && v.duration > 0) return true;
    } catch (e) { }
    return false;
  }

  function say() {
    var on = movieOn();
    if (on === null) return;
    var now = Date.now();
    if (now - lastSay < 700) return;
    lastSay = now;
    try {
      var to = (window.parent && window.parent !== window) ? window.parent : window;
      to.postMessage({ __un0tvState: { on: on } }, '*');
    } catch (e) { }
  }

  function act(cmd, at) {
    var v = biggest();
    if (!v) return;
    try {
      if (cmd === 'play') v.play();
      else if (cmd === 'pause') v.pause();
      else if (cmd === 'toggle') { if (v.paused) v.play(); else v.pause(); }
      else if (cmd === 'forward') v.currentTime = Math.max(0, (v.currentTime || 0) + 10);
      else if (cmd === 'rewind') v.currentTime = Math.max(0, (v.currentTime || 0) - 10);
      else if (cmd === 'seek' && typeof at === 'number') v.currentTime = Math.max(0, at);
      else if (cmd === 'mute') v.muted = !v.muted;
    } catch (e) { }
    say();
  }

  // A command, then the same command passed down to every frame below.
  function relay(cmd, at, hop) {
    act(cmd, at);
    if (hop >= 4) return;
    try {
      var list = document.querySelectorAll('iframe');
      for (var i = 0; i < list.length; i++) {
        var msg = { __un0tvCmd: cmd, time: at };
        msg[HOP] = hop + 1;
        list[i].contentWindow.postMessage(msg, '*');
      }
    } catch (e) { }
  }

  window.addEventListener('message', function (e) {
    var d = e.data;
    if (!d || !d.__un0tvCmd) return;
    relay(d.__un0tvCmd, d.time, (d[HOP] || 0) + 1);
  }, false);

  // Report at once when the movie starts, stops or goes away, and slowly in
  // between so a seek or a pause is not missed.
  function now() { lastSay = 0; say(); }
  try {
    document.addEventListener('playing', now, true);
    document.addEventListener('pause', now, true);
    document.addEventListener('ended', now, true);
    document.addEventListener('emptied', now, true);
    document.addEventListener('loadedmetadata', now, true);
  } catch (e) { }
  setInterval(say, 1200);
  say();
})();
""";
}
