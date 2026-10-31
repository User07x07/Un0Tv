namespace Un0Tv;

// Remote control for the VidFast movie player. The player sits in a frame from
// another website, so the app cannot touch it directly. VidFast's PostMessage
// API lets the page send it commands (play, pause, seek) and reports its state
// back (current time, playing or paused).
//
// This script runs in the page the viewer sees, finds the VidFast player frame,
// and exposes four simple actions the remote's keys call: play, pause, forward
// 10 seconds and back 10 seconds.
//
// It also saves watch progress from the player's events into the page's own
// browser storage (localStorage), so a movie or episode can resume where the
// viewer stopped the next time it is opened.
public static class PlayerControl
{
    public static readonly string Js = """
(function () {
  // Only the page the viewer sees drives the player; the player's own frame
  // is left alone.
  if (window.top !== window) return;
  if (window.__un0tvPlayer) return;

  // The VidFast domains, for example vidfast.vc or vidfast.pro.
  var VIDFAST = /(^|\.)vidfast\.[a-z]+$/i;
  var STORE = 'un0tvProgress';
  var time = 0;
  var playing = false;
  var resumedFor = null;
  var holdSaves = false;

  // Every VidFast player frame on the page.
  function players() {
    var out = [];
    var list = document.querySelectorAll('iframe');
    for (var i = 0; i < list.length; i++) {
      var f = list[i];
      try {
        var host = new URL(f.getAttribute('src') || '', location.href).hostname;
        if (VIDFAST.test(host) && f.contentWindow) out.push(f);
      } catch (e) { }
    }
    return out;
  }

  function send(msg) {
    var list = players();
    for (var i = 0; i < list.length; i++) {
      try { list[i].contentWindow.postMessage(msg, '*'); } catch (e) { }
    }
  }

  // ---- Saved progress ----

  function readStore() {
    try { return JSON.parse(localStorage.getItem(STORE) || '{}') || {}; } catch (e) { return {}; }
  }

  function writeStore(s) {
    try { localStorage.setItem(STORE, JSON.stringify(s)); } catch (e) { }
  }

  // One key per movie, and one per episode for shows.
  function keyFor(info) {
    if (!info.tmdbId) return null;
    if (info.mediaType === 'tv') {
      return 'tv' + info.tmdbId + ':s' + (info.season || 1) + 'e' + (info.episode || 1);
    }
    return 'movie' + info.tmdbId;
  }

  // The title VidFast has sent for this movie or show, if any.
  function titleFor(type, id) {
    try {
      var meta = JSON.parse(localStorage.getItem('vidFastProgress') || '{}') || {};
      var m = meta[(type === 'tv' ? 't' : 'm') + id];
      return m && m.title ? m.title : null;
    } catch (e) { return null; }
  }

  // Hands the saved entry to the app for the Continue watching row. Routine
  // updates go at most every 10 seconds per title; pauses and seeks go at once.
  var appKey = null;
  var appAt = 0;
  function forwardToApp(entry, key, force) {
    if (!window.Un0TvNative || !entry) return;
    var now = Date.now();
    if (!force && key === appKey && now - appAt < 10000) return;
    appKey = key;
    appAt = now;
    try {
      var copy = {};
      for (var k in entry) copy[k] = entry[k];
      copy.key = key;
      copy.title = titleFor(entry.type, entry.id);
      copy.url = location.href;
      window.Un0TvNative.saveProgress(JSON.stringify(copy));
    } catch (e) { }
  }

  function saveProgress(info) {
    var key = keyFor(info);
    if (!key || typeof info.currentTime !== 'number') return;
    var s = readStore();
    s[key] = {
      id: info.tmdbId,
      type: info.mediaType || 'movie',
      season: info.season || null,
      episode: info.episode || null,
      watched: Math.floor(info.currentTime),
      duration: Math.floor(info.duration || 0),
      url: location.href,
      updated: Date.now()
    };
    // Keep only the 60 most recent titles so storage never grows without limit.
    var keys = Object.keys(s);
    if (keys.length > 60) {
      keys.sort(function (a, b) { return s[a].updated - s[b].updated; });
      for (var i = 0; i < keys.length - 60; i++) delete s[keys[i]];
    }
    writeStore(s);
    forwardToApp(s[key], key, info.event === 'pause' || info.event === 'ended' || info.event === 'seeked');
  }

  // The first time a title is seen on this page, jump to the saved spot.
  // Saves are held back meanwhile, so the player's early zero time does not
  // overwrite the progress we are about to resume from.
  function resumeIfSaved(info) {
    var key = keyFor(info);
    if (!key || resumedFor === key) return;
    resumedFor = key;
    var saved = readStore()[key];
    if (!saved || saved.watched < 15) return;
    // Nearly finished: start again from the beginning.
    if (saved.duration && saved.watched > saved.duration - 60) return;
    holdSaves = true;
    setTimeout(function () {
      seek(saved.watched);
      holdSaves = false;
    }, 2000);
  }

  // The player reports its state back: current time, playing or paused.
  window.addEventListener('message', function (event) {
    var d = event.data;
    if (!d) return;
    try {
      if (!VIDFAST.test(new URL(event.origin).hostname)) return;
    } catch (e) { return; }
    // The title and poster details, kept for the Continue watching row.
    if (d.type === 'MEDIA_DATA' && d.data) {
      try { localStorage.setItem('vidFastProgress', JSON.stringify(d.data)); } catch (e) { }
      return;
    }
    if (d.type !== 'PLAYER_EVENT' || !d.data) return;
    var info = d.data;
    if (typeof info.currentTime === 'number') time = info.currentTime;
    if (typeof info.playing === 'boolean') playing = info.playing;
    if (info.event === 'play') playing = true;
    if (info.event === 'pause' || info.event === 'ended') playing = false;

    reportPlaying();

    if (info.tmdbId) {
      resumeIfSaved(info);
      if (!holdSaves) saveProgress(info);
    }
  });

  // Tells the app whether a movie is playing, so the remote's Left key knows
  // to open the player panel only while something is on.
  function reportPlaying() {
    try {
      if (window.Un0TvNative && window.Un0TvNative.mediaPlaying)
        window.Un0TvNative.mediaPlaying(playing ? 1 : 0);
    } catch (e) { }
  }

  function play() { playing = true; reportPlaying(); send({ command: 'play' }); }
  function pause() { playing = false; reportPlaying(); send({ command: 'pause' }); }

  function seek(to) {
    if (to < 0) to = 0;
    time = to;
    send({ command: 'seek', time: Math.floor(to) });
  }

  window.__un0tvPlayer = {
    play: play,
    pause: pause,
    toggle: function () { if (playing) pause(); else play(); },
    forward: function () { seek(time + 10); },
    back: function () { seek(time - 10); }
  };

  // Keep the current time fresh, so forward and back start from the right place.
  setInterval(function () { send({ command: 'getStatus' }); }, 1500);
})();
""";
}
